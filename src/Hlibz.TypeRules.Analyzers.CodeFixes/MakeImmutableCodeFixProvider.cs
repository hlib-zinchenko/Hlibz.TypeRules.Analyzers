using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR303: makes a field readonly, removes a private set accessor (or turns a wider one into
/// <c>init</c>, so object initializers keep working), or makes a struct a readonly struct. Each of
/// these breaks code that still assigns the state after construction, so the fix is only offered
/// after applying it speculatively shows no new compiler errors. Mutable collections get no fix:
/// the collection type and every method that changes it have to be rethought.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeImmutableCodeFixProvider))]
[Shared]
public sealed class MakeImmutableCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.TypeMustBeImmutableId);

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

        if (!diagnostic.Properties.TryGetValue(Immutability.ProblemProperty, out string? problem)
            || await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                is not { } root
            || await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                is not { } semanticModel)
        {
            return;
        }

        SyntaxNode? node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        (string Title, SyntaxNode Declaration, SyntaxNode Replacement)? change = problem switch
        {
            Immutability.MutableField => MakeFieldReadOnly(node, generator),
            Immutability.SetAccessor => RemoveOrInitSetter(node),
            Immutability.MutableStruct => MakeStructReadOnly(node, generator),
            _ => null,
        };

        if (change is not { } fix
            || semanticModel.GetDeclaredSymbol(
                    fix.Declaration is FieldDeclarationSyntax field
                        ? field.Declaration.Variables[0]
                        : fix.Declaration,
                    cancellationToken)
                is not { } symbol)
        {
            return;
        }

        Document changed = document.WithSyntaxRoot(
            root.ReplaceNode(fix.Declaration, fix.Replacement));

        if (!await SpeculativeCompilation.CompilesAsCleanlyAsync(
                    document,
                    changed.Project.Solution,
                    [symbol],
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: fix.Title,
                createChangedDocument: _ => Task.FromResult(changed),
                equivalenceKey: Descriptors.TypeMustBeImmutableId + problem),
            diagnostic);
    }

    /// <summary>
    /// Only a field declared on its own: <c>readonly</c> on <c>int a, b;</c> would change both.
    /// </summary>
    private static (string, SyntaxNode, SyntaxNode)? MakeFieldReadOnly(
        SyntaxNode? node,
        SyntaxGenerator generator)
    {
        if (node is not VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax
                {
                    Variables.Count: 1,
                    Parent: FieldDeclarationSyntax field,
                },
            } variable)
        {
            return null;
        }

        return (
            $"Make '{variable.Identifier.ValueText}' readonly",
            field,
            AddReadOnly(generator, field));
    }

    /// <summary>
    /// Auto-properties only: a set accessor with a body does something with the value, which has
    /// to be moved into a method by hand. A private one is removed (the constructor can still
    /// assign a get-only auto-property); a wider one becomes <c>init</c>.
    /// </summary>
    private static (string, SyntaxNode, SyntaxNode)? RemoveOrInitSetter(SyntaxNode? node)
    {
        if (node is not AccessorDeclarationSyntax
            {
                Parent: AccessorListSyntax { Parent: PropertyDeclarationSyntax property },
            } accessor
            || property.AccessorList!.Accessors.Any(candidate =>
                candidate.Body is not null || candidate.ExpressionBody is not null))
        {
            return null;
        }

        bool isPrivate = accessor.Modifiers.Any(SyntaxKind.PrivateKeyword)
            && !accessor.Modifiers.Any(SyntaxKind.ProtectedKeyword);

        if (isPrivate)
        {
            return (
                $"Remove the set accessor of '{property.Identifier.ValueText}'",
                property,
                property.RemoveNode(accessor, SyntaxRemoveOptions.KeepNoTrivia)!);
        }

        AccessorDeclarationSyntax init = SyntaxFactory.AccessorDeclaration(
                SyntaxKind.InitAccessorDeclaration,
                accessor.AttributeLists,
                accessor.Modifiers,
                SyntaxFactory.Token(SyntaxKind.InitKeyword).WithTriviaFrom(accessor.Keyword),
                accessor.Body,
                accessor.ExpressionBody,
                accessor.SemicolonToken)
            .WithTriviaFrom(accessor);

        return (
            $"Change the set accessor of '{property.Identifier.ValueText}' to init",
            property,
            property.ReplaceNode(accessor, init));
    }

    private static (string, SyntaxNode, SyntaxNode)? MakeStructReadOnly(
        SyntaxNode? node,
        SyntaxGenerator generator)
    {
        if (node is not TypeDeclarationSyntax declaration
            || declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
        {
            return null;
        }

        return (
            $"Make '{declaration.Identifier.ValueText}' a readonly struct",
            declaration,
            AddReadOnly(generator, declaration));
    }

    private static SyntaxNode AddReadOnly(SyntaxGenerator generator, SyntaxNode declaration)
    {
        return generator.WithModifiers(
            declaration,
            generator.GetModifiers(declaration).WithIsReadOnly(true));
    }
}
