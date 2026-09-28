using Hlibz.TypeRules.Analyzers.CodeFixes;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class MutableCollectionExposedTests
{
    private const string ReadOnlyCollections = """
        typerules.entities.match = T:App.Entity`1
        typerules.entities.readonly_collections = true
        """;

    [Fact]
    public async Task Analyze_WithExposedMutableCollections_ReportsEach()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Lines : List<string> { }

            public class Order : Entity<int>
            {
                public List<string> {|#0:Notes|} { get; } = [];

                public Dictionary<string, int> {|#1:Totals|} => new();

                protected int[] {|#2:codes|} = [];

                internal Lines {|#3:Lines|} { get; } = new();
            }
            """;

        await VerifyAnalyzerAsync(
            ReadOnlyCollections,
            source,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Notes", "List<string>", "'entities'"),
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(1)
                .WithArguments("Order.Totals", "Dictionary<string, int>", "'entities'"),
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(2)
                .WithArguments("Order.codes", "int[]", "'entities'"),
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(3)
                .WithArguments("Order.Lines", "Lines", "'entities'"));
    }

    [Fact]
    public async Task Analyze_WithPrivateOrReadOnlyCollections_ReportsNothing()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;

            namespace App;

            public sealed class Order : Entity<int>
            {
                private readonly List<string> _notes = [];

                public IReadOnlyList<string> Notes => _notes;

                public IEnumerable<string> Tags => _notes;

                public ImmutableArray<int> Codes { get; } = [];

                public ReadOnlyCollection<string> Audit => _notes.AsReadOnly();

                public string Name { get; } = "";
            }
            """;

        await VerifyAnalyzerAsync(ReadOnlyCollections, source);
    }

    [Fact]
    public async Task Fix_WithExpressionBodiedList_ExposesIReadOnlyList()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                private readonly List<string> _notes = [];

                public List<string> {|#0:Notes|} => _notes;
            }
            """;
        const string fixedSource = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                private readonly List<string> _notes = [];

                public IReadOnlyList<string> Notes => _notes;
            }
            """;

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            source,
            fixedSource,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Notes", "List<string>", "'entities'"));
    }

    [Fact]
    public async Task Fix_WithoutUsingDirective_AddsIt()
    {
        const string source = """
            namespace App;

            public sealed class Order : Entity<int>
            {
                public System.Collections.Generic.HashSet<string> {|#0:Tags|} { get; } = new();
            }
            """;
        const string fixedSource = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public IReadOnlySet<string> Tags { get; } = new HashSet<string>();
            }
            """;

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            source,
            fixedSource,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Tags", "HashSet<string>", "'entities'"));
    }

    [Fact]
    public async Task Fix_WithTargetTypedNew_NamesTheCollectionType()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public Dictionary<int, int> {|#0:Totals|} { get; } = new();
            }
            """;
        const string fixedSource = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public IReadOnlyDictionary<int, int> Totals { get; } = new Dictionary<int, int>();
            }
            """;

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            source,
            fixedSource,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Totals", "Dictionary<int, int>", "'entities'"));
    }

    [Fact]
    public async Task Fix_WithCallerMutatingCollection_OffersNoFix()
    {
        string[] sources =
        [
            """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public List<string> {|#0:Notes|} { get; } = [];
            }
            """,
            """
            namespace App;

            internal static class OrderNotes
            {
                public static void Add(Order order, string note) => order.Notes.Add(note);
            }
            """,
        ];

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            sources,
            sources,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Notes", "List<string>", "'entities'"));
    }

    /// <summary>
    /// A collection expression can't target IReadOnlySet&lt;T&gt;, so the change wouldn't compile:
    /// the speculative compilation catches it.
    /// </summary>
    [Fact]
    public async Task Fix_WithChangeThatWouldNotCompile_OffersNoFix()
    {
        const string source = """
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public HashSet<string> {|#0:Tags|} { get; } = [];
            }
            """;

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            source,
            source,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Tags", "HashSet<string>", "'entities'"));
    }

    [Fact]
    public async Task Fix_WithNonGenericOrMultiVariableField_OffersNoFix()
    {
        const string source = """
            using System.Collections;
            using System.Collections.Generic;

            namespace App;

            public sealed class Order : Entity<int>
            {
                public ArrayList {|#0:Legacy|} { get; } = [];

                public List<int> {|#1:a|} = [], {|#2:b|} = [];
            }
            """;

        await VerifyCodeFixAsync<ExposeReadOnlyCollectionCodeFixProvider>(
            ReadOnlyCollections,
            source,
            source,
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(0)
                .WithArguments("Order.Legacy", "ArrayList", "'entities'"),
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(1)
                .WithArguments("Order.a", "List<int>", "'entities'"),
            Diagnostic(Descriptors.MutableCollectionExposed)
                .WithLocation(2)
                .WithArguments("Order.b", "List<int>", "'entities'"));
    }
}
