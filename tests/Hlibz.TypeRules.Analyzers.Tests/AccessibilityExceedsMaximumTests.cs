using Hlibz.TypeRules.Analyzers.CodeFixes;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class AccessibilityExceedsMaximumTests
{
    private const string InternalEndpoints = """
        typerules.endpoints.match = T:App.IEndpoint
        typerules.endpoints.max_accessibility = internal
        """;

    [Fact]
    public async Task Analyze_WithPublicImplementation_ReportsTR001()
    {
        const string source = """
            namespace App;

            public sealed class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            InternalEndpoints,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithInternalImplementation_ReportsNothing()
    {
        const string source = """
            namespace App;

            internal sealed class GetUsersEndpoint : IEndpoint { }

            file sealed class LocalEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(InternalEndpoints, source);
    }

    [Fact]
    public async Task Analyze_WithIndirectImplementation_ReportsEveryPublicType()
    {
        const string source = """
            namespace App;

            public abstract class {|#0:EndpointBase|} : IEndpoint { }

            public sealed class {|#1:GetUsersEndpoint|} : EndpointBase { }
            """;

        await VerifyAnalyzerAsync(
            InternalEndpoints,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("EndpointBase", "public", "'endpoints'", "internal"),
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(1)
                .WithArguments("GetUsersEndpoint", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithOpenGenericBaseClass_MatchesEveryConstruction()
    {
        const string configuration = """
            typerules.entities.match = App.Entity`1
            typerules.entities.max_accessibility = internal
            """;
        const string source = """
            namespace App;

            public sealed class {|#0:Order|} : Entity<int> { }

            internal sealed class Customer : Entity<string> { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("Order", "public", "'entities'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithInterfaceDerivingMatchedInterface_ReportsNothing()
    {
        const string source = """
            namespace App;

            public interface IGetUsersEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(InternalEndpoints, source);
    }

    [Fact]
    public async Task Analyze_WithPublicTypeNestedInInternalType_UsesEffectiveAccessibility()
    {
        const string source = """
            namespace App;

            internal static class Users
            {
                public sealed class GetUsersEndpoint : IEndpoint { }
            }
            """;

        await VerifyAnalyzerAsync(InternalEndpoints, source);
    }

    [Fact]
    public async Task Analyze_WithProtectedNestedTypeAndInternalMaximum_ReportsTR001()
    {
        const string source = """
            namespace App;

            public class Users
            {
                protected sealed class {|#0:GetUsersEndpoint|} : IEndpoint { }
            }
            """;

        await VerifyAnalyzerAsync(
            InternalEndpoints,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "protected", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithPublicRecordStruct_ReportsTR001()
    {
        const string source = """
            namespace App;

            public record struct {|#0:Ping|} : IEndpoint;
            """;

        await VerifyAnalyzerAsync(
            InternalEndpoints,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("Ping", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithSeveralMatchingRuleSets_ReportsViolatedOnesAndCombinedMaximum()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.max_accessibility = internal
            typerules.handlers.match = T:App.IHandler
            typerules.handlers.max_accessibility = public
            """;
        const string source = """
            namespace App;

            public sealed class {|#0:GetUsers|} : IEndpoint, IHandler { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsers", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Analyze_WithoutConfiguration_ReportsNothing()
    {
        const string source = """
            namespace App;

            public sealed class GetUsersEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(string.Empty, source);
    }

    [Fact]
    public async Task Analyze_WithGeneratedCode_ReportsNothing()
    {
        const string source = """
            // <auto-generated/>
            namespace App;

            public sealed class GetUsersEndpoint : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(InternalEndpoints, source);
    }

    [Fact]
    public async Task Fix_WithPublicClass_MakesItInternal()
    {
        const string source = """
            namespace App;

            public sealed class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """;
        const string fixedSource = """
            namespace App;

            internal sealed class GetUsersEndpoint : IEndpoint { }
            """;

        await VerifyCodeFixAsync<RestrictAccessibilityCodeFixProvider>(
            InternalEndpoints,
            source,
            fixedSource,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Fix_WithPartialClassAcrossFiles_RewritesEveryPartStatingAccessibility()
    {
        string[] sources =
        [
            """
            namespace App;

            public sealed partial class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """,
            """
            namespace App;

            public partial class {|#1:GetUsersEndpoint|} { }
            """,
        ];
        string[] fixedSources =
        [
            """
            namespace App;

            internal sealed partial class GetUsersEndpoint : IEndpoint { }
            """,
            """
            namespace App;

            internal partial class GetUsersEndpoint { }
            """,
        ];

        await VerifyCodeFixAsync<RestrictAccessibilityCodeFixProvider>(
            InternalEndpoints,
            sources,
            fixedSources,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithLocation(1)
                .WithArguments("GetUsersEndpoint", "public", "'endpoints'", "internal"));
    }

    [Fact]
    public async Task Fix_WithTypeNestedInStruct_SkipsProtectedLevels()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.max_accessibility = protected internal
            """;
        const string source = """
            namespace App;

            public struct Users
            {
                public sealed class {|#0:GetUsersEndpoint|} : IEndpoint { }
            }
            """;
        const string fixedSource = """
            namespace App;

            public struct Users
            {
                internal sealed class GetUsersEndpoint : IEndpoint { }
            }
            """;

        await VerifyCodeFixAsync<RestrictAccessibilityCodeFixProvider>(
            configuration,
            source,
            fixedSource,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "public", "'endpoints'", "protected internal"));
    }

    [Fact]
    public async Task Fix_WithTopLevelTypeAndPrivateMaximum_OffersNoFix()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.max_accessibility = private
            """;
        const string source = """
            namespace App;

            internal sealed class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """;

        await VerifyCodeFixAsync<RestrictAccessibilityCodeFixProvider>(
            configuration,
            source,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "internal", "'endpoints'", "private"));
    }
}
