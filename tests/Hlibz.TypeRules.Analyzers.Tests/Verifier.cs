using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Hlibz.TypeRules.Analyzers.Tests;

/// <summary>
/// Runs <see cref="TypeRulesAnalyzer"/> (and optionally a code fix) over test sources, with
/// <c>typerules.*</c> configuration supplied as .editorconfig lines. Every test compilation also
/// contains <see cref="Contracts"/>, the base types the rule sets match on.
/// </summary>
internal static class Verifier
{
    public const string Contracts = """
        namespace App
        {
            public interface IEndpoint { }

            public interface IHandler { }

            public abstract class Entity<TId> { }

            public sealed class SealedBase { }

            public interface IAggregateRoot { }

            public interface IDto { }

            public interface IValueObject { }

            public abstract class ValueObject : IValueObject
            {
                public override bool Equals(object obj) => obj is ValueObject;

                public override int GetHashCode() => 0;
            }
        }

        namespace App.Contracts
        {
            public interface IRequestHandler { }

            public interface IRequestHandler<TRequest, TResponse> : IRequestHandler { }
        }
        """;

    private const string ContractsPath = "/0/Contracts.cs";

    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    public static Task VerifyAnalyzerAsync(
        string configuration,
        string source,
        params DiagnosticResult[] expected)
    {
        return VerifyAnalyzerAsync(configuration, [source], expected);
    }

    public static async Task VerifyAnalyzerAsync(
        string configuration,
        string[] sources,
        params DiagnosticResult[] expected)
    {
        CSharpAnalyzerTest<TypeRulesAnalyzer, DefaultVerifier> test = new()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        AddSources(test.TestState, sources);
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig(configuration)));
        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static Task VerifyCodeFixAsync<TCodeFix>(
        string configuration,
        string source,
        string fixedSource,
        params DiagnosticResult[] expected)
        where TCodeFix : CodeFixProvider, new()
    {
        return VerifyCodeFixAsync<TCodeFix>(configuration, [source], [fixedSource], expected);
    }

    /// <summary>
    /// Pass the fixed sources equal to the sources to assert that no fix is offered.
    /// </summary>
    public static Task VerifyCodeFixAsync<TCodeFix>(
        string configuration,
        string[] sources,
        string[] fixedSources,
        params DiagnosticResult[] expected)
        where TCodeFix : CodeFixProvider, new()
    {
        return VerifyCodeFixAsync<TCodeFix>(configuration, sources, fixedSources, [], expected);
    }

    /// <summary>
    /// <paramref name="addedFiles"/> are files the fix creates, by file name, next to the sources.
    /// </summary>
    public static async Task VerifyCodeFixAsync<TCodeFix>(
        string configuration,
        string[] sources,
        string[] fixedSources,
        (string FileName, string Content)[] addedFiles,
        params DiagnosticResult[] expected)
        where TCodeFix : CodeFixProvider, new()
    {
        CSharpCodeFixTest<TypeRulesAnalyzer, TCodeFix, DefaultVerifier> test = new()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        AddSources(test.TestState, sources);
        AddSources(test.FixedState, fixedSources);
        foreach ((string fileName, string content) in addedFiles)
        {
            test.FixedState.Sources.Add(($"/0/{fileName}", content));
        }

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig(configuration)));
        test.ExpectedDiagnostics.AddRange(expected);

        if (sources.SequenceEqual(fixedSources))
        {
            // No fix expected: the diagnostics stay after "fixing".
            test.FixedState.ExpectedDiagnostics.AddRange(expected);
            test.FixedState.MarkupHandling = MarkupMode.Allow;
        }

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs <typeparamref name="TCodeFix"/>'s Fix All over the solution with
    /// <paramref name="equivalenceKey"/>, the way <c>dotnet format</c> does: it passes the key of
    /// the first diagnostic's fix, or null when that diagnostic has no fix. (The test framework's
    /// own Fix All always passes a registered fix's key.) Asserts that the result compiles, and
    /// returns its documents by file name.
    /// </summary>
    public static async Task<Dictionary<string, SourceText>> FixAllAsync<TCodeFix>(
        string configuration,
        string[] sources,
        string? equivalenceKey)
        where TCodeFix : CodeFixProvider, new()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80
            .ResolveAsync(LanguageNames.CSharp, cancellationToken);

        using AdhocWorkspace workspace = new();
        Project project = workspace.AddProject("Test", LanguageNames.CSharp)
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithMetadataReferences(references);

        // Encoded the way files read from disk are: UTF-8 without a BOM.
        UTF8Encoding encoding = new(encoderShouldEmitUTF8Identifier: false);
        for (int i = 0; i < sources.Length; i++)
        {
            project = project.AddDocument(
                    $"Test{i}.cs",
                    SourceText.From(sources[i], encoding),
                    filePath: $"/0/Test{i}.cs")
                .Project;
        }

        project = project.AddDocument(
                "Contracts.cs",
                SourceText.From(Contracts, encoding),
                filePath: ContractsPath)
            .Project;
        project = project.AddAnalyzerConfigDocument(
                ".editorconfig",
                SourceText.From(EditorConfig(configuration)),
                filePath: "/.editorconfig")
            .Project;

        TCodeFix codeFix = new();
        Compilation compilation = (await project.GetCompilationAsync(cancellationToken))!;
        ImmutableArray<Diagnostic> diagnostics = (await compilation
                .WithAnalyzers([new TypeRulesAnalyzer()], project.AnalyzerOptions)
                .GetAnalyzerDiagnosticsAsync(cancellationToken))
            .Where(diagnostic => codeFix.FixableDiagnosticIds.Contains(diagnostic.Id))
            .ToImmutableArray();

        FixAllContext context = new(
            project.Solution.GetDocument(diagnostics[0].Location.SourceTree)!,
            codeFix,
            FixAllScope.Solution,
            equivalenceKey,
            codeFix.FixableDiagnosticIds,
            new FixedDiagnosticProvider(diagnostics),
            cancellationToken);

        Solution solution = project.Solution;
        CodeAction? action = await codeFix.GetFixAllProvider()!.GetFixAsync(context);
        if (action is not null)
        {
            ImmutableArray<CodeActionOperation> operations =
                await action.GetOperationsAsync(cancellationToken);
            solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        }

        Project fixedProject = solution.GetProject(project.Id)!;
        Compilation fixedCompilation = (await fixedProject.GetCompilationAsync(cancellationToken))!;
        Assert.Empty(fixedCompilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        Dictionary<string, SourceText> documents = [];
        foreach (Document document in fixedProject.Documents)
        {
            documents.Add(document.Name, await document.GetTextAsync(cancellationToken));
        }

        return documents;
    }

    private static void AddSources(SolutionState state, string[] sources)
    {
        for (int i = 0; i < sources.Length; i++)
        {
            state.Sources.Add(($"/0/Test{i}.cs", sources[i]));
        }

        state.Sources.Add((ContractsPath, Contracts));
    }

    private static string EditorConfig(string configuration)
    {
        return $"""
            root = true

            [*.cs]
            {configuration}
            """;
    }

    /// <summary>Hands Fix All the diagnostics computed up front.</summary>
    private sealed class FixedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken);
            return diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == tree);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(diagnostics.Where(diagnostic => !diagnostic.Location.IsInSource));
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        }
    }
}
