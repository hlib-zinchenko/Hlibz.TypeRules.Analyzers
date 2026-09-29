using System.Collections.Immutable;
using System.Composition;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR201: sets an explicit constructor's accessibility to the most permissive level its rule sets
/// allow. Anything outside the new level that still calls it stops compiling, so the fix is only
/// offered after applying it speculatively shows no new compiler errors. Implicit default and
/// primary constructors have no declaration of their own to change, and get no fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RestrictConstructorCodeFixProvider))]
[Shared]
public sealed class RestrictConstructorCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.ConstructorExceedsMaximumId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        Document document = context.Document;
        CancellationToken cancellationToken = context.CancellationToken;

        if (!diagnostic.Properties.TryGetValue(
                TypeRulesAnalyzer.MaxConstructorAccessibilityProperty,
                out string? maximumValue)
            || !int.TryParse(
                maximumValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximum)
            || await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                is not { } root
            || await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                is not { } semanticModel)
        {
            return;
        }

        // The implicit default constructor and primary constructors are reported on the type,
        // not inside a constructor declaration.
        ConstructorDeclarationSyntax? declaration = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .FirstAncestorOrSelf<ConstructorDeclarationSyntax>();

        if (declaration is null
            || semanticModel.GetDeclaredSymbol(declaration, cancellationToken)
                is not IMethodSymbol constructor
            || ChooseTarget(constructor.ContainingType, (AccessScope)maximum)
                is not { } target)
        {
            return;
        }

        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        Document changed = document.WithSyntaxRoot(root.ReplaceNode(
            declaration,
            generator.WithAccessibility(declaration, target.ToAccessibility())));

        // A parameterless constructor is also what new() constraints use, and those name the
        // type, not the constructor.
        ISymbol[] symbols = constructor.Parameters.IsEmpty
            ? [constructor, constructor.ContainingType]
            : [constructor];

        if (!await SpeculativeCompilation.CompilesAsCleanlyAsync(
                    document,
                    changed.Project.Solution,
                    symbols,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        string keyword = target.ToDisplayString();
        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Make the constructor {keyword}",
                createChangedDocument: _ => Task.FromResult(changed),
                equivalenceKey: Descriptors.ConstructorExceedsMaximumId + keyword),
            diagnostic);
    }

    /// <summary>
    /// The most permissive level within <paramref name="maximum"/>, with no protected level where
    /// the type can't declare new protected members.
    /// </summary>
    private static AccessScope? ChooseTarget(INamedTypeSymbol type, AccessScope maximum)
    {
        foreach (AccessScope candidate in AccessScopes.MostToLeastPermissive)
        {
            if (candidate.IsWithin(maximum)
                && (!Accessibilities.IsProtectedLevel(candidate)
                    || Accessibilities.AllowsNewProtectedMembers(type)))
            {
                return candidate;
            }
        }

        return null;
    }
}
