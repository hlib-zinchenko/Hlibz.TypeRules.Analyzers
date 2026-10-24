using Hlibz.TypeRules.Analyzers.CodeFixes;

using Microsoft.CodeAnalysis.Testing;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class TypeMustBeImmutableTests
{
    private const string ImmutableValueObjects = """
        typerules.value-objects.match = T:App.IValueObject
        typerules.value-objects.require_immutable = true
        """;

    [Fact]
    public async Task Analyze_WithMutableField_ReportsTR303()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                private decimal {|#0:_amount|};
            }
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Money._amount", "is a field that isn't readonly"));
    }

    [Fact]
    public async Task Analyze_WithSetAccessors_ReportsTR303()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public decimal Amount { get; {|#0:set|}; }

                public string Currency { get; private {|#1:set|}; } = "";
            }
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Money.Amount", "has a set accessor"),
            Immutable(1, "Money.Currency", "has a set accessor"));
    }

    [Fact]
    public async Task Analyze_WithMutableCollections_ReportsTR303EvenWhenPrivate()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Address : IValueObject
            {
                private readonly List<string> {|#0:_lines|} = [];

                public int[] {|#1:Codes|} { get; } = [];
            }
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Address._lines", "stores the mutable collection type 'List<string>'"),
            Immutable(1, "Address.Codes", "stores the mutable collection type 'int[]'"));
    }

    [Fact]
    public async Task Analyze_WithMutableStruct_ReportsTR303()
    {
        const string source = """
            namespace App;

            public struct {|#0:Money|} : IValueObject
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }
            }
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Money", "is a struct that isn't readonly"));
    }

    [Fact]
    public async Task Analyze_WithPositionalRecordStruct_ReportsStructAndParameters()
    {
        const string source = """
            namespace App;

            public record struct {|#0:Money|}(decimal {|#1:Amount|}) : IValueObject;
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Money", "is a struct that isn't readonly"),
            Immutable(1, "Money.Amount", "has a set accessor"));
    }

    [Fact]
    public async Task Analyze_WithImmutableTypes_ReportsNothing()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Collections.Immutable;

            namespace App;

            public sealed record Money(decimal Amount, string Currency) : IValueObject;

            public readonly record struct Percent(decimal Value) : IValueObject;

            public readonly struct Quantity : IValueObject
            {
                private readonly int _value;

                public Quantity(int value) => _value = value;
            }

            public sealed class Address : IValueObject
            {
                private static int s_created;

                private const int MaxLines = 4;

                private readonly IReadOnlyList<string> _lines = [];

                public ImmutableArray<string> Codes { get; init; }

                public int LineCount => _lines.Count;
            }
            """;

        await VerifyAnalyzerAsync(ImmutableValueObjects, source);
    }

    [Fact]
    public async Task Analyze_WithOverrideAndExplicitImplementation_ReportsOnlyTheDeclaration()
    {
        const string source = """
            namespace App;

            public interface IHasAmount
            {
                decimal Amount { get; set; }
            }

            public abstract class Measure : IValueObject
            {
                public abstract decimal Value { get; {|#0:set|}; }
            }

            public sealed class Length : Measure, IHasAmount
            {
                public override decimal Value { get; set; }

                decimal IHasAmount.Amount { get; set; }
            }
            """;

        await VerifyAnalyzerAsync(
            ImmutableValueObjects,
            source,
            Immutable(0, "Measure.Value", "has a set accessor"));
    }

    [Fact]
    public async Task Fix_WithFieldOnlyAssignedInConstructor_AddsReadOnly()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                private decimal {|#0:_amount|};

                public Money(decimal amount) => _amount = amount;
            }
            """;
        const string fixedSource = """
            namespace App;

            public sealed class Money : IValueObject
            {
                private readonly decimal _amount;

                public Money(decimal amount) => _amount = amount;
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            fixedSource,
            Immutable(0, "Money._amount", "is a field that isn't readonly"));
    }

    [Fact]
    public async Task Fix_WithFieldAssignedInMethod_OffersNoFix()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                private decimal {|#0:_amount|};

                public void Add(decimal amount) => _amount += amount;
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            source,
            Immutable(0, "Money._amount", "is a field that isn't readonly"));
    }

    [Fact]
    public async Task Fix_WithPrivateSetter_RemovesIt()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; private {|#0:set|}; }
            }
            """;
        const string fixedSource = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            fixedSource,
            Immutable(0, "Money.Amount", "has a set accessor"));
    }

    [Fact]
    public async Task Fix_WithPublicSetterUsedInObjectInitializer_ChangesItToInit()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public decimal Amount { get; {|#0:set|}; }
            }

            public static class Prices
            {
                public static Money Free() => new Money { Amount = 0 };
            }
            """;
        const string fixedSource = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public decimal Amount { get; init; }
            }

            public static class Prices
            {
                public static Money Free() => new Money { Amount = 0 };
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            fixedSource,
            Immutable(0, "Money.Amount", "has a set accessor"));
    }

    [Fact]
    public async Task Fix_WithSetterAssignedFromOtherFile_OffersNoFix()
    {
        const string money = """
            namespace App;

            public sealed class Money : IValueObject
            {
                public decimal Amount { get; {|#0:set|}; }
            }
            """;
        const string caller = """
            namespace App;

            public static class Prices
            {
                public static void Double(Money money) => money.Amount *= 2;
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            [money, caller],
            [money, caller],
            Immutable(0, "Money.Amount", "has a set accessor"));
    }

    [Fact]
    public async Task Fix_WithSetterWithBody_OffersNoFix()
    {
        const string source = """
            namespace App;

            public sealed class Money : IValueObject
            {
                private readonly decimal _amount;

                public decimal Amount { get => _amount; {|#0:set|} { } }
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            source,
            Immutable(0, "Money.Amount", "has a set accessor"));
    }

    [Fact]
    public async Task Fix_WithStructOfReadOnlyState_MakesItReadOnly()
    {
        const string source = """
            namespace App;

            public partial struct {|#0:Money|} : IValueObject
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }
            }
            """;
        const string fixedSource = """
            namespace App;

            public readonly partial struct Money : IValueObject
            {
                public Money(decimal amount) => Amount = amount;

                public decimal Amount { get; }
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            fixedSource,
            Immutable(0, "Money", "is a struct that isn't readonly"));
    }

    [Fact]
    public async Task Fix_WithStructWithMutableField_OffersNoFixForTheStruct()
    {
        const string source = """
            namespace App;

            public struct {|#0:Money|} : IValueObject
            {
                public decimal {|#1:Amount|};

                public void Clear() => Amount = 0;
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            source,
            Immutable(0, "Money", "is a struct that isn't readonly"),
            Immutable(1, "Money.Amount", "is a field that isn't readonly"));
    }

    [Fact]
    public async Task Fix_WithMutableCollection_OffersNoFix()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Address : IValueObject
            {
                private readonly List<string> {|#0:_lines|} = [];
            }
            """;

        await VerifyCodeFixAsync<MakeImmutableCodeFixProvider>(
            ImmutableValueObjects,
            source,
            source,
            Immutable(0, "Address._lines", "stores the mutable collection type 'List<string>'"));
    }

    private static DiagnosticResult Immutable(int location, string name, string problem)
    {
        return Diagnostic(Descriptors.TypeMustBeImmutable)
            .WithLocation(location)
            .WithArguments(name, problem, "'value-objects'");
    }
}
