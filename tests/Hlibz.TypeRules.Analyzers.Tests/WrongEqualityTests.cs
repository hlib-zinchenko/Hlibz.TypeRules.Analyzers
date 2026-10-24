using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class WrongEqualityTests
{
    private const string ValueEquality = """
        typerules.value-objects.match = T:App.IValueObject
        typerules.value-objects.equality = value
        """;

    // Records can only inherit from records, so an entity that's a record is one matched by an
    // interface.
    private const string IdentityEquality = """
        typerules.aggregates.match = T:App.IAggregateRoot
        typerules.aggregates.equality = identity
        """;

    [Fact]
    public async Task Analyze_WithClassWithoutEqualsForValue_ReportsTR202()
    {
        const string source = """
            namespace App;

            public sealed class {|#0:Money|} : IValueObject
            {
                public decimal Amount { get; }
            }
            """;

        await VerifyAnalyzerAsync(
            ValueEquality,
            source,
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(0)
                .WithArguments("Money", "compares by reference", "'value-objects'", "value"));
    }

    [Fact]
    public async Task Analyze_WithValueEqualityForValue_ReportsNothing()
    {
        const string source = """
            namespace App;

            public sealed record Money(decimal Amount) : IValueObject;

            public readonly record struct Percent(decimal Value) : IValueObject;

            public readonly struct Quantity : IValueObject { }

            // Inherits Equals from the ValueObject base class.
            public sealed class Currency : ValueObject { }

            public sealed class Country : IValueObject
            {
                public override bool Equals(object obj) => obj is Country;

                public override int GetHashCode() => 0;
            }
            """;

        await VerifyAnalyzerAsync(ValueEquality, source);
    }

    [Fact]
    public async Task Analyze_WithOnlyEquatableForValue_ReportsTR202()
    {
        const string source = """
            using System;

            namespace App;

            public sealed class {|#0:Money|} : IValueObject, IEquatable<Money>
            {
                public bool Equals(Money other) => true;
            }
            """;

        await VerifyAnalyzerAsync(
            ValueEquality,
            source,
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(0)
                .WithArguments("Money", "compares by reference", "'value-objects'", "value"));
    }

    [Fact]
    public async Task Analyze_WithRecordOrStructForIdentity_ReportsTR202()
    {
        const string source = """
            namespace App;

            public sealed record {|#0:Order|}(int Id) : IAggregateRoot;

            public struct {|#1:Invoice|} : IAggregateRoot { }

            public record struct {|#2:Shipment|} : IAggregateRoot;
            """;

        await VerifyAnalyzerAsync(
            IdentityEquality,
            source,
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(0)
                .WithArguments("Order", "is a record", "'aggregates'", "identity"),
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(1)
                .WithArguments("Invoice", "is a struct", "'aggregates'", "identity"),
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(2)
                .WithArguments("Shipment", "is a record struct", "'aggregates'", "identity"));
    }

    [Fact]
    public async Task Analyze_WithClassForIdentity_ReportsNothingEvenWhenEqualsComparesIds()
    {
        const string source = """
            namespace App;

            public sealed class Order : IAggregateRoot
            {
                public int Id { get; }

                public override bool Equals(object obj) => obj is Order other && other.Id == Id;

                public override int GetHashCode() => Id;
            }
            """;

        await VerifyAnalyzerAsync(IdentityEquality, source);
    }

    [Fact]
    public async Task Analyze_WithConflictingEqualities_ReportsTheUnmetOne()
    {
        const string configuration = ValueEquality + "\n" + IdentityEquality;
        const string source = """
            namespace App;

            public sealed record {|#0:Order|} : IValueObject, IAggregateRoot;
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.WrongEquality)
                .WithLocation(0)
                .WithArguments("Order", "is a record", "'aggregates'", "identity"));
    }
}
