using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR002: adds <c>sealed</c> to the type. Only offered when sealing can't break the build: nothing
/// in the solution derives from the type, and it declares no new virtual (CS0549) or protected
/// (CS0628) members.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SealTypeCodeFixProvider))]
[Shared]
public sealed class SealTypeCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.TypeMustBeSealedId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];

        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        SemanticModel? semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        TypeDeclarationSyntax? declaration = root?
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .FirstAncestorOrSelf<TypeDeclarationSyntax>();

        if (declaration is null
            || semanticModel?.GetDeclaredSymbol(declaration, context.CancellationToken)
                is not INamedTypeSymbol type
            || DeclaresOverridableOrProtectedMembers(type))
        {
            return;
        }

        IEnumerable<INamedTypeSymbol> derived = await SymbolFinder.FindDerivedClassesAsync(
                type,
                context.Document.Project.Solution,
                transitive: false,
                cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);

        if (derived.Any())
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Make '{type.Name}' sealed",
                createChangedDocument: cancellationToken => SealAsync(
                    context.Document,
                    declaration,
                    cancellationToken),
                equivalenceKey: Descriptors.TypeMustBeSealedId),
            diagnostic);
    }

    private static bool DeclaresOverridableOrProtectedMembers(INamedTypeSymbol type)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.IsOverride)
            {
                continue;
            }

            if (member.IsVirtual
                || member.DeclaredAccessibility is Accessibility.Protected
                    or Accessibility.ProtectedOrInternal
                    or Accessibility.ProtectedAndInternal)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<Document> SealAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken)
            .ConfigureAwait(false);

        DeclarationModifiers modifiers = editor.Generator.GetModifiers(declaration);
        editor.SetModifiers(declaration, modifiers.WithIsSealed(true));
        return editor.GetChangedDocument();
    }
}
