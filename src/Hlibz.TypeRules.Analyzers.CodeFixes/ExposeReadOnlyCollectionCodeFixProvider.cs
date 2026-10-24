using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR302: changes the member's declared type to its read-only equivalent, e.g.
/// <c>List&lt;T&gt;</c> to <c>IReadOnlyList&lt;T&gt;</c>. Changing a member's type can break code
/// that mutates it, so the fix is only offered after applying it speculatively shows no new
/// compiler errors in any document that references the member.
/// </summary>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(ExposeReadOnlyCollectionCodeFixProvider))]
[Shared]
public sealed class ExposeReadOnlyCollectionCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.MutableCollectionExposedId);

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

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        SyntaxNode? node = root?.FindNode(diagnostic.Location.SourceSpan);

        if (root is null
            || semanticModel is null
            || node is null
            || GetTypedDeclaration(node) is not { } declaration
            || semanticModel.GetDeclaredSymbol(node, cancellationToken) is not { } member
            || !CanChangeType(member)
            || GetMemberType(member) is not { } memberType
            || MutableCollections.Classify(memberType, out ITypeSymbol? collectionType)
                is not { } kind
            || collectionType is null
            || MutableCollections.GetReadOnlyEquivalent(
                    kind,
                    collectionType,
                    semanticModel.Compilation)
                is not { } readOnlyType)
        {
            return;
        }

        Document changed = await ChangeTypeAsync(
                document,
                root,
                declaration,
                memberType,
                readOnlyType,
                cancellationToken)
            .ConfigureAwait(false);

        if (!await SpeculativeCompilation.CompilesAsCleanlyAsync(
                    document,
                    changed.Project.Solution,
                    [member],
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        string displayName = readOnlyType.ToDisplayString(
            SymbolDisplayFormat.CSharpShortErrorMessageFormat);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Expose as '{displayName}'",
                createChangedDocument: _ => Task.FromResult(changed),
                equivalenceKey: Descriptors.MutableCollectionExposedId),
            diagnostic);
    }

    /// <summary>
    /// The property declaration, or the field's variable declaration when it declares only this
    /// one variable (changing the type of <c>List&lt;int&gt; a, b;</c> would change both).
    /// Positional record parameters have no declaration of their own to change.
    /// </summary>
    private static SyntaxNode? GetTypedDeclaration(SyntaxNode node)
    {
        return node switch
        {
            PropertyDeclarationSyntax property => property,
            VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Variables.Count: 1 } variableDeclaration,
                } => variableDeclaration,
            _ => null,
        };
    }

    private static bool CanChangeType(ISymbol member)
    {
        if (member is IPropertySymbol property)
        {
            if (property.IsVirtual || property.IsAbstract || property.IsOverride)
            {
                return false;
            }

            INamedTypeSymbol containingType = property.ContainingType;
            return !containingType.AllInterfaces
                .SelectMany(implemented => implemented.GetMembers())
                .Any(interfaceMember => SymbolEqualityComparer.Default.Equals(
                    containingType.FindImplementationForInterfaceMember(interfaceMember),
                    property));
        }

        return member is IFieldSymbol;
    }

    private static ITypeSymbol? GetMemberType(ISymbol member)
    {
        return member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
    }

    private static async Task<Document> ChangeTypeAsync(
        Document document,
        SyntaxNode root,
        SyntaxNode declaration,
        ITypeSymbol memberType,
        INamedTypeSymbol readOnlyType,
        CancellationToken cancellationToken)
    {
        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        TypeSyntax oldType = declaration switch
        {
            PropertyDeclarationSyntax property => property.Type,
            _ => ((VariableDeclarationSyntax)declaration).Type,
        };

        TypeSyntax newType = ((TypeSyntax)generator.TypeExpression(readOnlyType, addImport: true))
            .WithTriviaFrom(oldType)
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);

        // `= new()` is target-typed: with an interface as the target it no longer compiles, so
        // name the collection type it used to create.
        TypeSyntax createdType = ((TypeSyntax)generator.TypeExpression(memberType, addImport: true))
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);

        SyntaxNode newRoot = root.ReplaceNodes(
            declaration.DescendantNodesAndSelf().Where(node =>
                node == oldType || IsTargetTypedInitializer(node, declaration)),
            (original, _) => original == oldType
                ? newType
                : SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.Token(SyntaxKind.NewKeyword)
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        createdType,
                        ((ImplicitObjectCreationExpressionSyntax)original).ArgumentList,
                        ((ImplicitObjectCreationExpressionSyntax)original).Initializer)
                    .WithTriviaFrom(original));

        SourceText originalText = await document.GetTextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await DocumentCleanup.CleanUpAsync(
                document.WithSyntaxRoot(newRoot),
                DocumentCleanup.GetConsistentLineEnding(originalText),
                ensureFinalLineBreak: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsTargetTypedInitializer(SyntaxNode node, SyntaxNode declaration)
    {
        return node is ImplicitObjectCreationExpressionSyntax
            && node.Parent is EqualsValueClauseSyntax equalsValue
            && (equalsValue.Parent == declaration
                || equalsValue.Parent?.Parent == declaration);
    }
}
