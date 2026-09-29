using System.Collections.Immutable;
using System.Composition;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR301: restricts the set/init accessor to the most permissive level the rule sets allow, e.g.
/// <c>{ get; set; }</c> to <c>{ get; private set; }</c>. Not offered where an accessor modifier
/// would break the build: required members (CS9032), interface implementations, virtual,
/// abstract or override members, properties with no getter (CS0276), and accessors with no
/// syntax of their own (positional record parameters).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RestrictSetterCodeFixProvider))]
[Shared]
public sealed class RestrictSetterCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.SetterExceedsMaximumId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(
                MemberRules.MaxSetterAccessibilityProperty,
                out string? maximumValue)
            || !int.TryParse(
                maximumValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximum))
        {
            return;
        }

        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        SemanticModel? semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        AccessorDeclarationSyntax? accessor = root?
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .FirstAncestorOrSelf<AccessorDeclarationSyntax>();

        if (accessor is null
            || semanticModel?.GetDeclaredSymbol(accessor, context.CancellationToken)
                is not IMethodSymbol { AssociatedSymbol: IPropertySymbol property }
            || !CanRestrict(property))
        {
            return;
        }

        AccessScope? target = ChooseTarget(property, (AccessScope)maximum);
        if (target is not { } targetScope)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Make the {accessor.Keyword.ValueText} accessor "
                    + targetScope.ToDisplayString(),
                createChangedDocument: cancellationToken => RestrictAsync(
                    context.Document,
                    accessor,
                    targetScope,
                    cancellationToken),
                equivalenceKey: Descriptors.SetterExceedsMaximumId + targetScope.ToDisplayString()),
            diagnostic);
    }

    private static bool CanRestrict(IPropertySymbol property)
    {
        if (property.IsRequired
            || property.IsVirtual
            || property.IsAbstract
            || property.IsOverride
            || property.GetMethod is null)
        {
            return false;
        }

        INamedTypeSymbol containingType = property.ContainingType;
        foreach (INamedTypeSymbol implemented in containingType.AllInterfaces)
        {
            foreach (ISymbol interfaceMember in implemented.GetMembers())
            {
                if (SymbolEqualityComparer.Default.Equals(
                        containingType.FindImplementationForInterfaceMember(interfaceMember),
                        property))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The most permissive level within <paramref name="maximum"/> that an accessor may carry:
    /// strictly more restrictive than the property itself (CS0273), and no protected level where
    /// the containing type can't declare new protected members.
    /// </summary>
    private static AccessScope? ChooseTarget(IPropertySymbol property, AccessScope maximum)
    {
        AccessScope propertyScope = AccessScopes.FromAccessibility(property.DeclaredAccessibility);

        foreach (AccessScope candidate in AccessScopes.MostToLeastPermissive)
        {
            if (candidate.IsWithin(maximum)
                && candidate.IsWithin(propertyScope)
                && candidate != propertyScope
                && (!Accessibilities.IsProtectedLevel(candidate)
                    || Accessibilities.AllowsNewProtectedMembers(property.ContainingType)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<Document> RestrictAsync(
        Document document,
        AccessorDeclarationSyntax accessor,
        AccessScope target,
        CancellationToken cancellationToken)
    {
        SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false))!;

        List<SyntaxToken> modifiers = GetKeywords(target)
            .Select(kind => SyntaxFactory.Token(kind).WithTrailingTrivia(SyntaxFactory.Space))
            .ToList();
        modifiers.AddRange(accessor.Modifiers
            .Where(modifier => !SyntaxFacts.IsAccessibilityModifier(modifier.Kind()))
            .Select(modifier => modifier.WithoutTrivia().WithTrailingTrivia(SyntaxFactory.Space)));

        // The accessor's leading trivia sits on its first token, which is about to change.
        AccessorDeclarationSyntax restricted = accessor
            .WithModifiers(SyntaxFactory.TokenList(modifiers))
            .WithKeyword(accessor.Keyword.WithLeadingTrivia())
            .WithLeadingTrivia(accessor.GetLeadingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(accessor, restricted));
    }

    private static SyntaxKind[] GetKeywords(AccessScope scope)
    {
        return scope switch
        {
            AccessScope.Public => [SyntaxKind.PublicKeyword],
            AccessScope.ProtectedInternal =>
                [SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword],
            AccessScope.Internal => [SyntaxKind.InternalKeyword],
            AccessScope.Protected => [SyntaxKind.ProtectedKeyword],
            AccessScope.PrivateProtected =>
                [SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword],
            _ => [SyntaxKind.PrivateKeyword],
        };
    }
}
