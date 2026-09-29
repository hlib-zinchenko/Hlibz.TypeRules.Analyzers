using Hlibz.TypeRules.Analyzers.CodeFixes;

using Microsoft.CodeAnalysis.Testing;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class ConstructorExceedsMaximumTests
{
    private const string PrivateConstructors = """
        typerules.aggregates.match = T:App.IAggregateRoot
        typerules.aggregates.max_constructor_accessibility = private
        """;

    [Fact]
    public async Task Analyze_WithPublicConstructor_ReportsTR201()
    {
        const string source = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                public {|#0:Order|}(int id, string number) { }

                private Order() { }
            }
            """;

        await VerifyAnalyzerAsync(
            PrivateConstructors,
            source,
            Constructor(0, "Order(int, string)", "public"));
    }

    [Fact]
    public async Task Analyze_WithImplicitDefaultConstructor_ReportsTR201OnTheType()
    {
        const string source = """
            namespace App;

            public sealed class {|#0:Order|} : IAggregateRoot { }
            """;

        await VerifyAnalyzerAsync(
            PrivateConstructors,
            source,
            Constructor(0, "Order()", "public"));
    }

    [Fact]
    public async Task Analyze_WithPrimaryConstructors_ReportsTR201()
    {
        const string source = """
            namespace App;

            public sealed record {|#0:Order|}(int Id) : IAggregateRoot;

            public sealed class {|#1:Invoice|}(int id) : IAggregateRoot
            {
                public int Id { get; } = id;
            }
            """;

        await VerifyAnalyzerAsync(
            PrivateConstructors,
            source,
            Constructor(0, "Order(int)", "public"),
            Constructor(1, "Invoice(int)", "public"));
    }

    [Fact]
    public async Task Analyze_WithInternalConstructorInInternalType_UsesEffectiveAccessibility()
    {
        const string configuration = """
            typerules.aggregates.match = T:App.IAggregateRoot
            typerules.aggregates.max_constructor_accessibility = internal
            """;
        const string source = """
            namespace App;

            internal sealed class Order : IAggregateRoot
            {
                public Order() { }
            }
            """;

        await VerifyAnalyzerAsync(configuration, source);
    }

    [Fact]
    public async Task Analyze_WithSkippedConstructors_ReportsNothing()
    {
        const string source = """
            namespace App;

            // Abstract: its constructors are for derived types.
            public abstract class AggregateBase : IAggregateRoot
            {
                public AggregateBase() { }
            }

            // Struct: the implicit parameterless constructor can't be restricted.
            public readonly struct Snapshot : IAggregateRoot
            {
                private Snapshot(int version) { }
            }

            // Record: its implicit copy constructor only serves 'with' expressions.
            public sealed record Order : IAggregateRoot
            {
                private Order() { }

                public static Order Create() => new();
            }

            public static class Aggregates
            {
                public static Order Rename(Order order) => order with { };
            }
            """;

        await VerifyAnalyzerAsync(PrivateConstructors, source);
    }

    [Fact]
    public async Task Analyze_WithSeveralRuleSets_CombinesMaximums()
    {
        const string configuration = """
            typerules.aggregates.match = T:App.IAggregateRoot
            typerules.aggregates.max_constructor_accessibility = internal
            typerules.entities.match = T:App.Entity`1
            typerules.entities.max_constructor_accessibility = protected
            """;
        const string source = """
            namespace App;

            public class Order : Entity<int>, IAggregateRoot
            {
                public {|#0:Order|}() { }
            }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.ConstructorExceedsMaximum)
                .WithLocation(0)
                .WithArguments(
                    "Order()",
                    "public",
                    "'aggregates', 'entities'",
                    "private protected"));
    }

    [Fact]
    public async Task Fix_WithConstructorOnlyCalledFromFactory_RestrictsIt()
    {
        const string source = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                public {|#0:Order|}(int id) { }

                public static Order Place(int id) => new(id);
            }
            """;
        const string fixedSource = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                private Order(int id) { }

                public static Order Place(int id) => new(id);
            }
            """;

        await VerifyCodeFixAsync<RestrictConstructorCodeFixProvider>(
            PrivateConstructors,
            source,
            fixedSource,
            Constructor(0, "Order(int)", "public"));
    }

    [Fact]
    public async Task Fix_WithConstructorCalledFromOtherType_OffersNoFix()
    {
        const string order = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                public {|#0:Order|}(int id) { }
            }
            """;
        const string caller = """
            namespace App;

            public static class Orders
            {
                public static Order Place() => new Order(1);
            }
            """;

        await VerifyCodeFixAsync<RestrictConstructorCodeFixProvider>(
            PrivateConstructors,
            [order, caller],
            [order, caller],
            Constructor(0, "Order(int)", "public"));
    }

    [Fact]
    public async Task Fix_WithParameterlessConstructorUsedByNewConstraint_OffersNoFix()
    {
        const string order = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                public {|#0:Order|}() { }
            }
            """;
        const string caller = """
            namespace App;

            public static class Factory
            {
                public static T Create<T>() where T : new() => new T();

                public static Order Order() => Create<Order>();
            }
            """;

        await VerifyCodeFixAsync<RestrictConstructorCodeFixProvider>(
            PrivateConstructors,
            [order, caller],
            [order, caller],
            Constructor(0, "Order()", "public"));
    }

    [Fact]
    public async Task Fix_WithImplicitConstructor_OffersNoFix()
    {
        const string source = """
            namespace App;

            public sealed class {|#0:Order|} : IAggregateRoot { }
            """;

        await VerifyCodeFixAsync<RestrictConstructorCodeFixProvider>(
            PrivateConstructors,
            source,
            source,
            Constructor(0, "Order()", "public"));
    }

    private static DiagnosticResult Constructor(int location, string name, string accessibility)
    {
        return Diagnostic(Descriptors.ConstructorExceedsMaximum)
            .WithLocation(location)
            .WithArguments(name, accessibility, "'aggregates'", "private");
    }
}
