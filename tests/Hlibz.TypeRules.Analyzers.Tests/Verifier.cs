using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

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
}
