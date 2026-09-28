using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class ForbiddenMemberTypeTests
{
    private const string Aggregates = """
        typerules.aggregates.match = T:App.IAggregateRoot
        typerules.aggregates.forbid_member_types = aggregates
        """;

    [Fact]
    public async Task Analyze_WithNavigationToAnotherAggregate_ReportsTR007()
    {
        const string source = """
            namespace App;

            public sealed class Customer : IAggregateRoot { }

            public sealed class Order : IAggregateRoot
            {
                public Customer {|#0:Customer|} { get; private set; } = new();
            }
            """;

        await VerifyAnalyzerAsync(
            Aggregates,
            source,
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(0)
                .WithArguments("Order.Customer", "Customer", "aggregates", "aggregates"));
    }

    [Fact]
    public async Task Analyze_WithAggregateInsideCollectionsAndWrappers_ReportsEach()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : IAggregateRoot { }

            public sealed class Customer : IAggregateRoot
            {
                private readonly List<Order> {|#0:_orders|} = [];

                private Dictionary<Guid, Order> {|#1:_byId|} = [];

                public Order[] {|#2:Recent|} { get; } = [];

                private Lazy<Order>? {|#3:_latest|};

                private (Order Order, int Rank) {|#4:_best|};

                private IAggregateRoot? {|#5:_related|};
            }
            """;

        await VerifyAnalyzerAsync(
            Aggregates,
            source,
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(0)
                .WithArguments("Customer._orders", "Order", "aggregates", "aggregates"),
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(1)
                .WithArguments("Customer._byId", "Order", "aggregates", "aggregates"),
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(2)
                .WithArguments("Customer.Recent", "Order", "aggregates", "aggregates"),
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(3)
                .WithArguments("Customer._latest", "Order", "aggregates", "aggregates"),
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(4)
                .WithArguments("Customer._best", "Order", "aggregates", "aggregates"),
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(5)
                .WithArguments("Customer._related", "IAggregateRoot", "aggregates", "aggregates"));
    }

    [Fact]
    public async Task Analyze_WithReferenceById_ReportsNothing()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace App;

            public sealed class Customer : IAggregateRoot { }

            public sealed class Order : IAggregateRoot
            {
                private readonly List<OrderLine> _lines = [];

                public Guid CustomerId { get; private set; }

                public IReadOnlyList<OrderLine> Lines => _lines;

                public void AssignTo(Customer customer) { }
            }

            public sealed class OrderLine { }
            """;

        await VerifyAnalyzerAsync(Aggregates, source);
    }

    [Fact]
    public async Task Analyze_WithComputedPropertyOverField_ReportsOnlyTheField()
    {
        const string source = """
            namespace App;

            public sealed class Customer : IAggregateRoot { }

            public sealed class Order : IAggregateRoot
            {
                private Customer? {|#0:_customer|};

                public Customer? Customer => _customer;
            }
            """;

        await VerifyAnalyzerAsync(
            Aggregates,
            source,
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(0)
                .WithArguments("Order._customer", "Customer", "aggregates", "aggregates"));
    }

    [Fact]
    public async Task Analyze_WithSelfReference_AllowedUnlessDisabled()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Category : IAggregateRoot
            {
                public Category? {|#0:Parent|} { get; private set; }
            }
            """;
        const string strict = Aggregates + "\ntyperules.aggregates.allow_self_references = false";

        await VerifyAnalyzerAsync(Aggregates, source.Replace("{|#0:Parent|}", "Parent"));
        await VerifyAnalyzerAsync(
            strict,
            source,
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(0)
                .WithArguments("Category.Parent", "Category", "aggregates", "aggregates"));
    }

    [Fact]
    public async Task Analyze_WithTargetRuleSetWithoutConstraints_ChecksAcrossRuleSets()
    {
        const string configuration = """
            typerules.dtos.match = T:App.IDto
            typerules.dtos.forbid_member_types = aggregates
            typerules.aggregates.match = T:App.IAggregateRoot
            """;
        const string source = """
            namespace App;

            public sealed class Order : IAggregateRoot { }

            public sealed record OrderDto(Order {|#0:Order|}) : IDto;
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.ForbiddenMemberType)
                .WithLocation(0)
                .WithArguments("OrderDto.Order", "Order", "aggregates", "dtos"));
    }

    [Fact]
    public async Task Parse_WithUnknownForbiddenRuleSet_ReportsTR000()
    {
        const string configuration = """
            typerules.dtos.match = T:App.IDto
            typerules.dtos.forbid_member_types = entities
            """;
        const string source = """
            namespace App;

            public sealed class OrderDto : IDto { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'dtos' forbids member types of rule set 'entities', which isn't "
                + "configured"));
    }

    [Fact]
    public async Task Parse_WithAllowSelfReferencesAlone_ReportsTR000()
    {
        const string configuration = """
            typerules.aggregates.match = T:App.IAggregateRoot
            typerules.aggregates.require_sealed = true
            typerules.aggregates.allow_self_references = false
            """;
        const string source = """
            namespace App;

            public sealed class Order : IAggregateRoot { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'aggregates' sets allow_self_references without forbid_member_types, "
                + "which it modifies"));
    }
}
