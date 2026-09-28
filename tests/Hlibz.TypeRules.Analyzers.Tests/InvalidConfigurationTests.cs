using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

/// <summary>
/// TR000 has no source location: configuration lives in .editorconfig, which diagnostics can't
/// point into. Every test also checks that an invalid rule set is ignored rather than half-applied:
/// the sources would violate it if it were applied.
/// </summary>
public sealed class InvalidConfigurationTests
{
    private const string PublicUnsealedEndpoint = """
        namespace App;

        public class GetUsersEndpoint : IEndpoint { }
        """;

    [Fact]
    public async Task Parse_WithUnknownOption_ReportsTR000AndIgnoresRuleSet()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_seald = true
            typerules.endpoints.max_accessibility = internal
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "unknown option 'require_seald' in rule set 'endpoints' "
                + "(known options: match, max_accessibility, require_sealed, "
                + "companion_interface)"));
    }

    [Fact]
    public async Task Parse_WithoutMatch_ReportsTR000()
    {
        const string configuration = "typerules.endpoints.require_sealed = true";

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration)
                .WithArguments("rule set 'endpoints' has no match option"));
    }

    [Fact]
    public async Task Parse_WithoutConstraint_ReportsTR000()
    {
        const string configuration = "typerules.endpoints.match = T:App.IEndpoint";

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' sets no constraint "
                + "(expected max_accessibility, require_sealed, companion_interface)"));
    }

    [Fact]
    public async Task Parse_WithInvalidValues_ReportsEveryProblem()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.max_accessibility = intern
            typerules.endpoints.require_sealed = yes
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' has an invalid max_accessibility 'intern' (expected public, "
                + "protected_internal, internal, protected, private_protected or private)"),
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' has an invalid require_sealed 'yes' "
                + "(expected true or false)"));
    }

    [Fact]
    public async Task Parse_WithNonTypeDocumentationId_ReportsTR000()
    {
        const string configuration = """
            typerules.endpoints.match = M:App.IEndpoint.Handle
            typerules.endpoints.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' matches 'M:App.IEndpoint.Handle', which is not a type "
                + "(expected T:Namespace.TypeName)"));
    }

    [Fact]
    public async Task Parse_WithMisspelledTypeInKnownNamespace_ReportsTR000()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpont
            typerules.endpoints.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' matches type 'T:App.IEndpont', which was not found"));
    }

    [Fact]
    public async Task Parse_WithTypeFromUnreferencedAssembly_IgnoresItSilently()
    {
        const string configuration = """
            typerules.endpoints.match = T:Some.Other.Library.IEndpoint | T:App.IEndpoint
            typerules.endpoints.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            """
            namespace App;

            internal class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "'endpoints'"));
    }

    [Fact]
    public async Task Parse_WithSealedMatchedType_ReportsTR000()
    {
        const string configuration = """
            typerules.sealed.match = T:App.SealedBase
            typerules.sealed.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'sealed' matches 'T:App.SealedBase', which no type can inherit from "
                + "or implement"));
    }

    [Fact]
    public async Task Parse_WithKeyMissingOption_ReportsTR000()
    {
        const string configuration = """
            typerules.endpoints = T:App.IEndpoint
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "key 'typerules.endpoints' must have the form typerules.<rule set>.<option>"));
    }

    [Fact]
    public async Task Parse_WithManyTypes_ReportsEachErrorOnce()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            """;
        const string source = """
            namespace App;

            internal class A { }

            internal class B { }

            internal class C { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'endpoints' sets no constraint "
                + "(expected max_accessibility, require_sealed, companion_interface)"));
    }

    [Fact]
    public async Task Parse_WithBareTypeNamesAndSpacedAccessibility_AppliesRuleSet()
    {
        const string configuration = """
            typerules.handlers.match = App.IEndpoint, App.IHandler
            typerules.handlers.max_accessibility = private protected
            """;
        const string source = """
            namespace App;

            internal class Users
            {
                internal sealed class {|#0:GetUsersHandler|} : IHandler { }

                private protected sealed class GetUsersEndpoint : IEndpoint { }
            }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.AccessibilityExceedsMaximum)
                .WithLocation(0)
                .WithArguments("GetUsersHandler", "internal", "'handlers'", "private protected"));
    }
}
