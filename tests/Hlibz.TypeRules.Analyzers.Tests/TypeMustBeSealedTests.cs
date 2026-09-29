using Hlibz.TypeRules.Analyzers.CodeFixes;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class TypeMustBeSealedTests
{
    private const string SealedEndpoints = """
        typerules.endpoints.match = T:App.IEndpoint
        typerules.endpoints.require_sealed = true
        """;

    [Fact]
    public async Task Analyze_WithUnsealedClass_ReportsTR102()
    {
        const string source = """
            namespace App;

            internal class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            SealedEndpoints,
            source,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "'endpoints'"));
    }

    [Fact]
    public async Task Analyze_WithSealedAbstractOrStructTypes_ReportsNothing()
    {
        const string source = """
            namespace App;

            internal sealed class GetUsersEndpoint : IEndpoint { }

            internal abstract class EndpointBase : IEndpoint { }

            internal struct Ping : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(SealedEndpoints, source);
    }

    [Fact]
    public async Task Analyze_WithRequireSealedFalse_ReportsNothing()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_sealed = false
            """;
        const string source = """
            namespace App;

            internal class GetUsersEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(configuration, source);
    }

    [Fact]
    public async Task Fix_WithUnsealedClass_AddsSealed()
    {
        const string source = """
            namespace App;

            internal partial class {|#0:GetUsersEndpoint|} : IEndpoint
            {
                public override string ToString() => "users";
            }
            """;
        const string fixedSource = """
            namespace App;

            internal sealed partial class GetUsersEndpoint : IEndpoint
            {
                public override string ToString() => "users";
            }
            """;

        await VerifyCodeFixAsync<SealTypeCodeFixProvider>(
            SealedEndpoints,
            source,
            fixedSource,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "'endpoints'"));
    }

    [Fact]
    public async Task Fix_WithRecord_AddsSealed()
    {
        const string source = """
            namespace App;

            public record {|#0:GetUsers|}(int Page) : IEndpoint;
            """;
        const string fixedSource = """
            namespace App;

            public sealed record GetUsers(int Page) : IEndpoint;
            """;

        await VerifyCodeFixAsync<SealTypeCodeFixProvider>(
            SealedEndpoints,
            source,
            fixedSource,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsers", "'endpoints'"));
    }

    [Fact]
    public async Task Fix_WithDerivedClass_OffersNoFix()
    {
        const string source = """
            namespace App;

            internal class {|#0:EndpointBase|} : IEndpoint { }

            internal sealed class GetUsersEndpoint : EndpointBase { }
            """;

        await VerifyCodeFixAsync<SealTypeCodeFixProvider>(
            SealedEndpoints,
            source,
            source,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("EndpointBase", "'endpoints'"));
    }

    [Fact]
    public async Task Fix_WithVirtualOrProtectedMember_OffersNoFix()
    {
        const string source = """
            namespace App;

            internal class {|#0:GetUsersEndpoint|} : IEndpoint
            {
                public virtual string Route => "users";
            }

            internal class {|#1:GetOrdersEndpoint|} : IEndpoint
            {
                protected string Route => "orders";
            }
            """;

        await VerifyCodeFixAsync<SealTypeCodeFixProvider>(
            SealedEndpoints,
            source,
            source,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "'endpoints'"),
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(1)
                .WithArguments("GetOrdersEndpoint", "'endpoints'"));
    }
}
