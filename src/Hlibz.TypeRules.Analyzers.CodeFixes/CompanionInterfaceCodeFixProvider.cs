using System.Collections.Immutable;
using System.Composition;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// TR104. When the companion interface doesn't exist, generates it and moves the matched
/// interfaces from the type's base list onto it. When it exists but isn't implemented, adds it to
/// the base list. No fix when a companion exists but doesn't extend a matched interface, or when a
/// non-interface type already has the companion's name: what to do there is the author's call.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CompanionInterfaceCodeFixProvider))]
[Shared]
public sealed class CompanionInterfaceCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Descriptors.CompanionInterfaceMissingId);

    private const string ImplementKey = Descriptors.CompanionInterfaceMissingId + "Implement";
    private const string ExtractKey = Descriptors.CompanionInterfaceMissingId + "Extract";

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        // Not the batch fixer: it merges edits to existing documents and drops added ones, so
        // "fix all" would never create the interface files.
        return CompanionFixAllProvider.Instance;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        string? equivalenceKey = GetEquivalenceKey(diagnostic);
        if (equivalenceKey is null)
        {
            return;
        }

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
                is not INamedTypeSymbol type)
        {
            return;
        }

        string displayName = CompanionInterfaces.GetDisplayName(type);
        string title;
        if (equivalenceKey == ImplementKey)
        {
            title = $"Implement '{displayName}'";
        }
        else if (GetBaseInterfaces(type, diagnostic, semanticModel.Compilation).Count > 0)
        {
            title = $"Extract companion interface '{displayName}'";
        }
        else
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => ApplyAsync(
                    context.Document,
                    declaration,
                    type,
                    diagnostic,
                    cancellationToken),
                equivalenceKey),
            diagnostic);
    }

    private static string? GetEquivalenceKey(Diagnostic diagnostic)
    {
        diagnostic.Properties.TryGetValue(CompanionInterfaces.StateProperty, out string? state);
        return state switch
        {
            CompanionInterfaces.NotImplementedState => ImplementKey,
            CompanionInterfaces.MissingState => ExtractKey,
            _ => null,
        };
    }

    private static async Task<Solution> ApplyAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol type,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        if (GetEquivalenceKey(diagnostic) == ImplementKey)
        {
            Document implemented = await ImplementAsync(
                    document,
                    declaration,
                    CompanionInterfaces.GetDisplayName(type),
                    cancellationToken)
                .ConfigureAwait(false);
            return implemented.Project.Solution;
        }

        Compilation? compilation = await document.Project.GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        List<INamedTypeSymbol> baseInterfaces = compilation is null
            ? []
            : GetBaseInterfaces(type, diagnostic, compilation);

        if (baseInterfaces.Count == 0)
        {
            return document.Project.Solution;
        }

        return await ExtractAsync(document, declaration, type, baseInterfaces, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<INamedTypeSymbol> GetBaseInterfaces(
        INamedTypeSymbol type,
        Diagnostic diagnostic,
        Compilation compilation)
    {
        diagnostic.Properties.TryGetValue(
            CompanionInterfaces.MatchedTypesProperty,
            out string? matchedTypeIds);

        List<INamedTypeSymbol> matchedTypes = (matchedTypeIds ?? string.Empty)
            .Split(['|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(id => DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation))
            .OfType<INamedTypeSymbol>()
            .ToList();

        return GetBaseInterfaces(type, matchedTypes);
    }

    /// <summary>
    /// The interfaces the companion should extend: the type's own direct interfaces that are (or
    /// extend) a matched interface, as written, e.g. <c>IRequestHandler&lt;GetUsers, User&gt;</c>.
    /// If the type only gets them through its base class, the most specific inherited ones.
    /// </summary>
    private static List<INamedTypeSymbol> GetBaseInterfaces(
        INamedTypeSymbol type,
        List<INamedTypeSymbol> matchedTypes)
    {
        List<INamedTypeSymbol> direct = type.Interfaces
            .Where(implemented => matchedTypes.Any(matchedType =>
                IsOrDerivesFrom(implemented, matchedType)))
            .ToList();

        if (direct.Count > 0)
        {
            return direct;
        }

        // Only the most specific ones: IRequestHandler<int, string> rather than also the
        // non-generic IRequestHandler it extends, so the companion exposes the useful contract.
        List<INamedTypeSymbol> inherited = type.AllInterfaces
            .Where(implemented => matchedTypes.Any(matchedType =>
                IsOrDerivesFrom(implemented, matchedType)))
            .ToList();

        return inherited
            .Where(candidate => !inherited.Any(other =>
                other.AllInterfaces.Contains(candidate, SymbolEqualityComparer.Default)))
            .ToList();
    }

    private static bool IsOrDerivesFrom(INamedTypeSymbol type, INamedTypeSymbol matchedType)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, matchedType)
            || TypeRuleSet.IsDerivedFrom(type, matchedType);
    }

    private static async Task<Document> ImplementAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        string companionName,
        CancellationToken cancellationToken)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken)
            .ConfigureAwait(false);

        editor.AddInterfaceType(declaration, SyntaxFactory.ParseTypeName(companionName));
        return editor.GetChangedDocument();
    }

    private static async Task<Solution> ExtractAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol type,
        List<INamedTypeSymbol> baseInterfaces,
        CancellationToken cancellationToken)
    {
        SemanticModel semanticModel = (await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false))!;
        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        LanguageVersion languageVersion =
            ((CSharpParseOptions)declaration.SyntaxTree.Options).LanguageVersion;

        InterfaceDeclarationSyntax companion = CreateCompanion(
            declaration,
            type,
            baseInterfaces,
            generator,
            languageVersion);

        TypeDeclarationSyntax implementing = ReplaceBaseInterfaces(
            declaration,
            semanticModel,
            baseInterfaces,
            SyntaxFactory.ParseTypeName(CompanionInterfaces.GetDisplayName(type)),
            cancellationToken);

        SourceText originalText = await document.GetTextAsync(cancellationToken)
            .ConfigureAwait(false);
        string? lineEnding = DocumentCleanup.GetConsistentLineEnding(originalText);

        string fileName = CompanionInterfaces.GetName(type) + ".cs";
        string? directory = Path.GetDirectoryName(document.FilePath);
        string? filePath = directory is null ? null : Path.Combine(directory, fileName);

        // Generic and nested types keep the companion in their own file: constraint clauses are
        // copied as written, and may need that file's using directives.
        bool ownFile = type.ContainingType is null
            && type.Arity == 0
            && !document.Project.Documents.Any(existing =>
                string.Equals(existing.FilePath, filePath, StringComparison.Ordinal)
                || (filePath is null && existing.Name == fileName
                    && existing.Folders.SequenceEqual(document.Folders)));

        if (!ownFile)
        {
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken)
                .ConfigureAwait(false);
            editor.InsertBefore(declaration, companion);
            editor.ReplaceNode(declaration, implementing);

            Document changed = await DocumentCleanup.CleanUpAsync(
                    editor.GetChangedDocument(),
                    lineEnding,
                    ensureFinalLineBreak: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return changed.Project.Solution;
        }

        SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false))!;
        Document original = document.WithSyntaxRoot(root.ReplaceNode(declaration, implementing));

        CompilationUnitSyntax companionRoot = CreateCompilationUnit(
            root,
            type.ContainingNamespace,
            companion,
            languageVersion);

        Document added = original.Project.AddDocument(
            fileName,
            companionRoot,
            document.Folders,
            filePath);

        added = await DocumentCleanup.CleanUpAsync(
                added,
                lineEnding,
                ensureFinalLineBreak: true,
                cancellationToken)
            .ConfigureAwait(false);
        return added.Project.Solution;
    }

    private static InterfaceDeclarationSyntax CreateCompanion(
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol type,
        List<INamedTypeSymbol> baseInterfaces,
        SyntaxGenerator generator,
        LanguageVersion languageVersion)
    {
        Accessibility accessibility = type.ContainingType is not null
            ? type.DeclaredAccessibility
            : baseInterfaces.All(IsPubliclyVisible) ? Accessibility.Public : Accessibility.Internal;

        IEnumerable<BaseTypeSyntax> baseTypes = baseInterfaces.Select(baseInterface =>
            SyntaxFactory.SimpleBaseType(
                ((TypeSyntax)generator.TypeExpression(baseInterface, addImport: true))
                    .WithAdditionalAnnotations(
                        Simplifier.Annotation,
                        Simplifier.AddImportsAnnotation)));
        BaseListSyntax baseList = SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(baseTypes));

        // Keep whatever separated the base list from the constraint clauses (usually a line break).
        if (declaration.BaseList is not null)
        {
            baseList = baseList.WithTrailingTrivia(declaration.BaseList.GetTrailingTrivia());
        }

        InterfaceDeclarationSyntax companion = SyntaxFactory
            .InterfaceDeclaration(CompanionInterfaces.GetName(type))
            .WithTypeParameterList(declaration.TypeParameterList?.WithoutTrivia())
            .WithBaseList(baseList)
            .WithConstraintClauses(declaration.ConstraintClauses);

        // C# 12 allows a type declaration with no body, which is how a marker-style companion
        // reads best (`public interface IFoo : IBar;`).
        if (languageVersion >= LanguageVersion.CSharp12)
        {
            companion = companion
                .WithOpenBraceToken(default)
                .WithCloseBraceToken(default)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        return ((InterfaceDeclarationSyntax)generator.WithAccessibility(companion, accessibility))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    /// Replaces the base-list entries for <paramref name="baseInterfaces"/> with the companion
    /// (appended when the type only inherits them). Edited as a list rather than through the
    /// formatter, which would also reflow the author's own braces and line breaks; only
    /// separators this creates get a space after them.
    /// </summary>
    private static TypeDeclarationSyntax ReplaceBaseInterfaces(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        List<INamedTypeSymbol> baseInterfaces,
        TypeSyntax companionType,
        CancellationToken cancellationToken)
    {
        BaseListSyntax baseList = declaration.BaseList
            ?? SyntaxFactory.BaseList().WithLeadingTrivia(SyntaxFactory.Space);
        SeparatedSyntaxList<BaseTypeSyntax> types = baseList.Types;

        List<int> removed = [];
        for (int i = 0; i < types.Count; i++)
        {
            ITypeSymbol? symbol = semanticModel.GetTypeInfo(types[i].Type, cancellationToken).Type;
            if (baseInterfaces.Contains(symbol, SymbolEqualityComparer.Default))
            {
                removed.Add(i);
            }
        }

        BaseTypeSyntax companion = SyntaxFactory.SimpleBaseType(companionType);
        if (removed.Count > 0)
        {
            // The companion takes the first removed entry's place, trivia and separator included,
            // so the author's layout survives; the other removed entries just go.
            for (int i = removed.Count - 1; i >= 1; i--)
            {
                types = types.RemoveAt(removed[i]);
            }

            types = types.Replace(types[removed[0]], companion.WithTriviaFrom(types[removed[0]]));
        }
        else
        {
            if (types.Count > 0)
            {
                BaseTypeSyntax last = types[types.Count - 1];
                types = types.Replace(last, last.WithoutTrailingTrivia());
            }

            types = types.Add(companion);
        }

        foreach (SyntaxToken separator in types.GetSeparators().ToList())
        {
            if (!separator.HasTrailingTrivia)
            {
                types = types.ReplaceSeparator(
                    separator,
                    separator.WithTrailingTrivia(SyntaxFactory.Space));
            }
        }

        // The base list keeps its original trailing trivia (e.g. the line break before a
        // constraint clause), which a removed last entry may have carried.
        SyntaxTriviaList trailing = declaration.BaseList?.GetTrailingTrivia()
            ?? SyntaxFactory.TriviaList(SyntaxFactory.Space);

        return declaration.WithBaseList(
            baseList.WithTypes(types).WithoutTrailingTrivia().WithTrailingTrivia(trailing));
    }

    /// <summary>
    /// The new file's root, with the same namespace declaration style (file-scoped or block) as the
    /// file the type is declared in.
    /// </summary>
    private static CompilationUnitSyntax CreateCompilationUnit(
        SyntaxNode originalRoot,
        INamespaceSymbol containingNamespace,
        InterfaceDeclarationSyntax companion,
        LanguageVersion languageVersion)
    {
        CompilationUnitSyntax unit = SyntaxFactory.CompilationUnit();
        if (containingNamespace.IsGlobalNamespace)
        {
            return unit.AddMembers(companion);
        }

        NameSyntax name = SyntaxFactory.ParseName(containingNamespace.ToDisplayString());
        bool fileScoped = languageVersion >= LanguageVersion.CSharp10
            && originalRoot.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Any();

        MemberDeclarationSyntax namespaceDeclaration = fileScoped
            ? SyntaxFactory.FileScopedNamespaceDeclaration(name).AddMembers(companion)
            : SyntaxFactory.NamespaceDeclaration(name).AddMembers(companion);

        return unit.AddMembers(
            namespaceDeclaration.WithAdditionalAnnotations(Formatter.Annotation));
    }

    /// <summary>
    /// Whether a public interface may extend <paramref name="type"/> (CS0061 otherwise): the type
    /// and every type argument in it must be public.
    /// </summary>
    private static bool IsPubliclyVisible(ITypeSymbol type)
    {
        return type switch
        {
            IArrayTypeSymbol array => IsPubliclyVisible(array.ElementType),
            INamedTypeSymbol named => AccessScopes.GetEffective(named) == AccessScope.Public
                && named.TypeArguments.All(IsPubliclyVisible),
            _ => true,
        };
    }

    /// <summary>
    /// Applies the fixes one at a time, each against the solution the previous one produced.
    /// Every type is found again by its documentation ID rather than by position, because earlier
    /// fixes add using directives and files that move or replace what the diagnostics point at.
    /// </summary>
    private sealed class CompanionFixAllProvider : FixAllProvider
    {
        public static readonly CompanionFixAllProvider Instance = new();

        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes()
        {
            return [FixAllScope.Document, FixAllScope.Project, FixAllScope.Solution];
        }

        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            string title = fixAllContext.CodeActionEquivalenceKey == ImplementKey
                ? "Implement companion interfaces"
                : "Extract companion interfaces";

            return Task.FromResult<CodeAction?>(CodeAction.Create(
                title,
                cancellationToken => FixAllAsync(fixAllContext, cancellationToken),
                fixAllContext.CodeActionEquivalenceKey));
        }

        private static async Task<Solution> FixAllAsync(
            FixAllContext context,
            CancellationToken cancellationToken)
        {
            List<Diagnostic> diagnostics = [];
            switch (context.Scope)
            {
                case FixAllScope.Document when context.Document is not null:
                    diagnostics.AddRange(await context.GetDocumentDiagnosticsAsync(context.Document)
                        .ConfigureAwait(false));
                    break;
                case FixAllScope.Project:
                    diagnostics.AddRange(await context.GetAllDiagnosticsAsync(context.Project)
                        .ConfigureAwait(false));
                    break;
                case FixAllScope.Solution:
                    foreach (Project project in context.Solution.Projects)
                    {
                        diagnostics.AddRange(await context.GetAllDiagnosticsAsync(project)
                            .ConfigureAwait(false));
                    }

                    break;
            }

            Solution solution = context.Solution;
            List<(DocumentId DocumentId, string TypeId, Diagnostic Diagnostic)> work = [];

            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (GetEquivalenceKey(diagnostic) != context.CodeActionEquivalenceKey
                    || solution.GetDocument(diagnostic.Location.SourceTree) is not { } document)
                {
                    continue;
                }

                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken)
                    .ConfigureAwait(false);
                SemanticModel? semanticModel = await document
                    .GetSemanticModelAsync(cancellationToken)
                    .ConfigureAwait(false);
                TypeDeclarationSyntax? declaration = root?
                    .FindToken(diagnostic.Location.SourceSpan.Start)
                    .Parent?
                    .FirstAncestorOrSelf<TypeDeclarationSyntax>();

                if (declaration is not null
                    && semanticModel?.GetDeclaredSymbol(declaration, cancellationToken)
                        is INamedTypeSymbol type
                    && DocumentationCommentId.CreateDeclarationId(type) is { } typeId)
                {
                    work.Add((document.Id, typeId, diagnostic));
                }
            }

            foreach ((DocumentId documentId, string typeId, Diagnostic diagnostic) in work)
            {
                if (solution.GetDocument(documentId) is not { } document
                    || await document.Project.GetCompilationAsync(cancellationToken)
                        .ConfigureAwait(false) is not { } compilation
                    || DocumentationCommentId.GetFirstSymbolForDeclarationId(typeId, compilation)
                        is not INamedTypeSymbol type)
                {
                    continue;
                }

                SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken)
                    .ConfigureAwait(false);
                SyntaxReference? reference = type.DeclaringSyntaxReferences
                    .FirstOrDefault(candidate => candidate.SyntaxTree == tree);

                if (reference is not null
                    && await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false)
                        is TypeDeclarationSyntax declaration)
                {
                    solution = await ApplyAsync(
                            document,
                            declaration,
                            type,
                            diagnostic,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return solution;
        }
    }
}
