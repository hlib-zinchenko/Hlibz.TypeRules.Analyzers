using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class WrongNamespaceTests
{
    private const string Endpoints = """
        typerules.endpoints.match = T:App.IEndpoint
        typerules.endpoints.namespace_pattern = **.Endpoints | **.Endpoints.*
        """;

    [Fact]
    public async Task Analyze_WithTypeInMatchingNamespace_ReportsNothing()
    {
        const string source = """
            namespace App.Users.Endpoints
            {
                internal sealed class GetUserEndpoint : IEndpoint { }
            }

            namespace App.Endpoints.Orders
            {
                internal sealed class GetOrderEndpoint : IEndpoint { }
            }
            """;

        await VerifyAnalyzerAsync(Endpoints, source);
    }

    [Fact]
    public async Task Analyze_WithTypeInOtherNamespace_ReportsTR006()
    {
        const string source = """
            namespace App.Users;

            internal sealed class {|#0:GetUserEndpoint|} : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            Endpoints,
            source,
            Diagnostic(Descriptors.WrongNamespace)
                .WithLocation(0)
                .WithArguments(
                    "GetUserEndpoint",
                    "namespace 'App.Users'",
                    "endpoints",
                    "'**.Endpoints' or '**.Endpoints.*'"));
    }

    [Fact]
    public async Task Analyze_WithTypeInGlobalNamespace_ReportsTR006()
    {
        const string source = """
            internal sealed class {|#0:GetUserEndpoint|} : App.IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            Endpoints,
            source,
            Diagnostic(Descriptors.WrongNamespace)
                .WithLocation(0)
                .WithArguments(
                    "GetUserEndpoint",
                    "the global namespace",
                    "endpoints",
                    "'**.Endpoints' or '**.Endpoints.*'"));
    }

    [Fact]
    public async Task Analyze_WithNestedType_UsesContainingNamespace()
    {
        const string source = """
            namespace App.Endpoints
            {
                internal static class Users
                {
                    internal sealed class GetUserEndpoint : IEndpoint { }
                }
            }
            """;

        await VerifyAnalyzerAsync(Endpoints, source);
    }

    [Fact]
    public async Task Analyze_WithSeveralViolatedRuleSets_ReportsEachSeparately()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.namespace_pattern = **.Endpoints
            typerules.handlers.match = T:App.IHandler
            typerules.handlers.namespace_pattern = **.Handlers
            """;
        const string source = """
            namespace App.Users;

            internal sealed class {|#0:GetUser|} : IEndpoint, IHandler { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.WrongNamespace)
                .WithLocation(0)
                .WithArguments("GetUser", "namespace 'App.Users'", "endpoints", "'**.Endpoints'"),
            Diagnostic(Descriptors.WrongNamespace)
                .WithLocation(0)
                .WithArguments("GetUser", "namespace 'App.Users'", "handlers", "'**.Handlers'"));
    }

    [Fact]
    public async Task Parse_WithInvalidNamespacePattern_ReportsTR000()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.namespace_pattern = App..Endpoints
            """;
        const string source = """
            namespace App.Users;

            internal sealed class GetUserEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' has an invalid namespace_pattern 'App..Endpoints' (expected "
                + "dot-separated names, with '*' for one segment and '**' for any number, "
                + "alternatives separated by '|')"));
    }
}
