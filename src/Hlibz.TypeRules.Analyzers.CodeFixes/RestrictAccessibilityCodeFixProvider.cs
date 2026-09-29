using System.Collections.Immutable;
using System.Composition;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR101: sets the type's declared accessibility to the most permissive level its rule sets
/// allow, on every partial declaration that states one.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RestrictAccessibilityCodeFixProvider))]
[Shared]
public sealed class RestrictAccessibilityCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.AccessibilityExceedsMaximumId);

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
                TypeRulesAnalyzer.MaxAccessibilityProperty,
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

        BaseTypeDeclarationSyntax? declaration = root?
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();

        if (declaration is null
            || semanticModel?.GetDeclaredSymbol(declaration, context.CancellationToken)
                is not INamedTypeSymbol type)
        {
            return;
        }

        AccessScope? target = ChooseTarget(type, (AccessScope)maximum);
        if (target is not { } targetScope)
        {
            return;
        }

        Accessibility accessibility = targetScope.ToAccessibility();
        string keyword = targetScope.ToDisplayString();

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Make '{type.Name}' {keyword}",
                createChangedSolution: cancellationToken => SetAccessibilityAsync(
                    context.Document.Project.Solution,
                    type,
                    accessibility,
                    cancellationToken),
                equivalenceKey: Descriptors.AccessibilityExceedsMaximumId + keyword),
            diagnostic);
    }

    /// <summary>
    /// The most permissive accessibility that fits within <paramref name="maximum"/> and is legal
    /// where the type is declared, or null when no legal level fits (e.g. a top-level type whose
    /// rule set allows only private).
    /// </summary>
    private static AccessScope? ChooseTarget(INamedTypeSymbol type, AccessScope maximum)
    {
        INamedTypeSymbol? container = type.ContainingType;

        foreach (AccessScope candidate in AccessScopes.MostToLeastPermissive)
        {
            if (!candidate.IsWithin(maximum))
            {
                continue;
            }

            bool isLegal = container is null
                ? candidate is AccessScope.Public or AccessScope.Internal
                : !Accessibilities.IsProtectedLevel(candidate)
                    || Accessibilities.AllowsNewProtectedMembers(container);

            if (isLegal)
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<Solution> SetAccessibilityAsync(
        Solution solution,
        INamedTypeSymbol type,
        Accessibility accessibility,
        CancellationToken cancellationToken)
    {
        List<SyntaxNode> declarations = [];
        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            SyntaxNode declaration = await reference.GetSyntaxAsync(cancellationToken)
                .ConfigureAwait(false);
            declarations.Add(declaration);
        }

        // Partial parts may each state the accessibility or leave it to another part. Rewrite the
        // parts that state one; if none does, the first part gets it.
        List<SyntaxNode> toRewrite = declarations
            .Where(declaration => ((MemberDeclarationSyntax)declaration).Modifiers
                .Any(modifier => SyntaxFacts.IsAccessibilityModifier(modifier.Kind())))
            .ToList();

        if (toRewrite.Count == 0 && declarations.Count > 0)
        {
            toRewrite.Add(declarations[0]);
        }

        foreach (IGrouping<SyntaxTree, SyntaxNode> group
            in toRewrite.GroupBy(node => node.SyntaxTree))
        {
            Document? document = solution.GetDocument(group.Key);
            if (document is null)
            {
                continue;
            }

            SyntaxNode root = await group.Key.GetRootAsync(cancellationToken).ConfigureAwait(false);
            SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
            SyntaxNode newRoot = root.ReplaceNodes(
                group,
                (_, rewritten) => generator.WithAccessibility(rewritten, accessibility));

            solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        }

        return solution;
    }
}
