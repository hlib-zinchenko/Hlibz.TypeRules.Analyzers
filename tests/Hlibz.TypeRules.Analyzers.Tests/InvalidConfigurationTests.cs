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
            ConfigurationError(
                "unknown option 'require_seald' in rule set 'endpoints' "
                + "(known options: match, max_accessibility, require_sealed, "
                + "companion_interface, max_setter_accessibility, allow_init, "
                + "readonly_collections, namespace_pattern, forbid_member_types, "
                + "allow_self_references, require_immutable, equality, "
                + "max_constructor_accessibility, enabled)"));
    }

    [Fact]
    public async Task Parse_WithoutMatch_ReportsTR000()
    {
        const string configuration = "typerules.endpoints.require_sealed = true";

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError("rule set 'endpoints' has no match option"));
    }

    [Fact]
    public async Task Parse_WithoutConstraint_ReportsTR000()
    {
        const string configuration = "typerules.endpoints.match = T:App.IEndpoint";

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'endpoints' sets no constraint "
                + "(expected max_accessibility, require_sealed, companion_interface, "
                + "max_setter_accessibility, readonly_collections, namespace_pattern, "
                + "forbid_member_types, require_immutable, equality, "
                + "max_constructor_accessibility)"));
    }

    [Fact]
    public async Task Parse_WithInvalidValues_ReportsEveryProblem()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.max_accessibility = intern
            typerules.endpoints.require_sealed = yes
            typerules.endpoints.equality = structural
            typerules.endpoints.max_constructor_accessibility = privat
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'endpoints' has an invalid max_accessibility 'intern' (expected public, "
                + "protected_internal, internal, protected, private_protected or private)"),
            ConfigurationError(
                "rule set 'endpoints' has an invalid require_sealed 'yes' "
                + "(expected true or false)"),
            ConfigurationError(
                "rule set 'endpoints' has an invalid equality 'structural' (expected value or "
                + "identity)"),
            ConfigurationError(
                "rule set 'endpoints' has an invalid max_constructor_accessibility 'privat' "
                + "(expected public, protected_internal, internal, protected, private_protected "
                + "or private)"));
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
            ConfigurationError(
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
            ConfigurationError(
                "rule set 'endpoints' matches type 'T:App.IEndpont', which was not found "
                + "(did you mean 'T:App.IEndpoint'?)"));
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
            ConfigurationError(
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
            ConfigurationError(
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
            ConfigurationError(
                "rule set 'endpoints' sets no constraint "
                + "(expected max_accessibility, require_sealed, companion_interface, "
                + "max_setter_accessibility, readonly_collections, namespace_pattern, "
                + "forbid_member_types, require_immutable, equality, "
                + "max_constructor_accessibility)"));
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

    [Fact]
    public async Task Parse_WithMisspelledGenericType_SuggestsTheClosestType()
    {
        const string configuration = """
            typerules.handlers.match = T:App.Contracts.IRequestHandlr`2
            typerules.handlers.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'handlers' matches type 'T:App.Contracts.IRequestHandlr`2', which was "
                + "not found (did you mean 'T:App.Contracts.IRequestHandler`2'?)"));
    }

    [Fact]
    public async Task Parse_WithMissingTypeUnlikeAnyOther_SuggestsNothing()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.Controllers
            typerules.endpoints.require_sealed = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'endpoints' matches type 'T:App.Controllers', which was not found"));
    }

    [Fact]
    public async Task Parse_WithMisspelledOnlyConstraint_ReportsOnlyTheUnknownOption()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_seald = true
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "unknown option 'require_seald' in rule set 'endpoints' "
                + "(known options: match, max_accessibility, require_sealed, "
                + "companion_interface, max_setter_accessibility, allow_init, "
                + "readonly_collections, namespace_pattern, forbid_member_types, "
                + "allow_self_references, require_immutable, equality, "
                + "max_constructor_accessibility, enabled)"));
    }

    [Fact]
    public async Task Parse_WithRuleSetDisabled_IgnoresIt()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_sealed = true
            typerules.endpoints.max_accessibility = internal
            typerules.endpoints.enabled = false
            """;

        await VerifyAnalyzerAsync(configuration, PublicUnsealedEndpoint);
    }

    [Fact]
    public async Task Parse_WithRuleSetEnabled_AppliesIt()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_sealed = true
            typerules.endpoints.enabled = true
            """;
        const string source = """
            namespace App;

            public class {|#0:GetUsersEndpoint|} : IEndpoint { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.TypeMustBeSealed)
                .WithLocation(0)
                .WithArguments("GetUsersEndpoint", "'endpoints'"));
    }

    [Fact]
    public async Task Parse_WithOnlyEnabled_ReportsTR000()
    {
        const string configuration = "typerules.endponts.enabled = false";

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'endponts' only sets enabled, but no rule set 'endponts' is "
                + "configured"));
    }

    [Fact]
    public async Task Parse_WithInvalidEnabled_ReportsTR000AndIgnoresRuleSet()
    {
        const string configuration = """
            typerules.endpoints.match = T:App.IEndpoint
            typerules.endpoints.require_sealed = true
            typerules.endpoints.enabled = no
            """;

        await VerifyAnalyzerAsync(
            configuration,
            PublicUnsealedEndpoint,
            ConfigurationError(
                "rule set 'endpoints' has an invalid enabled 'no' (expected true or false)"));
    }
}
