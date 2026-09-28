using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>What a mutable collection type is, which decides its read-only equivalent.</summary>
internal enum MutableCollectionKind
{
    /// <summary>Indexable: arrays, <c>List&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, ...</summary>
    List,

    /// <summary>
    /// <c>Dictionary&lt;TKey, TValue&gt;</c>, <c>IDictionary&lt;TKey, TValue&gt;</c>, ...
    /// </summary>
    Dictionary,

    /// <summary><c>HashSet&lt;T&gt;</c>, <c>ISet&lt;T&gt;</c>, ...</summary>
    Set,

    /// <summary>Everything else generic: <c>ICollection&lt;T&gt;</c>, queues, stacks, ...</summary>
    Collection,

    /// <summary>
    /// Non-generic collections and multi-dimensional arrays: no read-only equivalent.
    /// </summary>
    Other,
}

/// <summary>
/// TR005's definition of a mutable collection. Deliberately a list of known types rather than
/// "implements ICollection&lt;T&gt;": immutable and read-only collections (ImmutableArray,
/// ReadOnlyCollection) implement IList&lt;T&gt; too, with mutators that throw.
/// </summary>
internal static class MutableCollections
{
    private static readonly Dictionary<string, MutableCollectionKind> KnownTypes =
        new(StringComparer.Ordinal)
        {
            ["System.Collections.Generic.List`1"] = MutableCollectionKind.List,
            ["System.Collections.Generic.IList`1"] = MutableCollectionKind.List,
            ["System.Collections.ObjectModel.Collection`1"] = MutableCollectionKind.List,
            ["System.Collections.ObjectModel.ObservableCollection`1"] = MutableCollectionKind.List,
            ["System.Collections.Generic.Dictionary`2"] = MutableCollectionKind.Dictionary,
            ["System.Collections.Generic.IDictionary`2"] = MutableCollectionKind.Dictionary,
            ["System.Collections.Generic.SortedDictionary`2"] = MutableCollectionKind.Dictionary,
            ["System.Collections.Generic.SortedList`2"] = MutableCollectionKind.Dictionary,
            ["System.Collections.Concurrent.ConcurrentDictionary`2"] =
                MutableCollectionKind.Dictionary,
            ["System.Collections.Generic.HashSet`1"] = MutableCollectionKind.Set,
            ["System.Collections.Generic.ISet`1"] = MutableCollectionKind.Set,
            ["System.Collections.Generic.SortedSet`1"] = MutableCollectionKind.Set,
            ["System.Collections.Generic.ICollection`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Generic.LinkedList`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Generic.Queue`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Generic.Stack`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Concurrent.ConcurrentBag`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Concurrent.ConcurrentQueue`1"] = MutableCollectionKind.Collection,
            ["System.Collections.Concurrent.ConcurrentStack`1"] = MutableCollectionKind.Collection,
            ["System.Collections.ArrayList"] = MutableCollectionKind.Other,
            ["System.Collections.Hashtable"] = MutableCollectionKind.Other,
            ["System.Collections.IList"] = MutableCollectionKind.Other,
            ["System.Collections.IDictionary"] = MutableCollectionKind.Other,
        };

    /// <summary>
    /// Classifies <paramref name="type"/>, or returns null if it isn't a mutable collection.
    /// <paramref name="collectionType"/> is the known type it is or derives from, constructed, e.g.
    /// <c>List&lt;OrderLine&gt;</c> for a class deriving from it; its type arguments are the
    /// read-only equivalent's.
    /// </summary>
    public static MutableCollectionKind? Classify(ITypeSymbol type, out ITypeSymbol? collectionType)
    {
        collectionType = null;

        if (type is IArrayTypeSymbol array)
        {
            collectionType = array;
            return array.Rank == 1 ? MutableCollectionKind.List : MutableCollectionKind.Other;
        }

        for (INamedTypeSymbol? current = type as INamedTypeSymbol;
             current is not null;
             current = current.BaseType)
        {
            if (KnownTypes.TryGetValue(GetMetadataName(current), out MutableCollectionKind kind))
            {
                collectionType = current;
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// The read-only interface to expose instead, or null when there's none (non-generic
    /// collections, multi-dimensional arrays). Sets become IReadOnlySet&lt;T&gt; where it exists
    /// (.NET 5+), IReadOnlyCollection&lt;T&gt; otherwise.
    /// </summary>
    public static INamedTypeSymbol? GetReadOnlyEquivalent(
        MutableCollectionKind kind,
        ITypeSymbol collectionType,
        Compilation compilation)
    {
        ITypeSymbol[] typeArguments = collectionType switch
        {
            IArrayTypeSymbol array => [array.ElementType],
            INamedTypeSymbol named => named.TypeArguments.ToArray(),
            _ => [],
        };

        string? metadataName = kind switch
        {
            MutableCollectionKind.List => "System.Collections.Generic.IReadOnlyList`1",
            MutableCollectionKind.Dictionary => "System.Collections.Generic.IReadOnlyDictionary`2",
            MutableCollectionKind.Set
                when compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlySet`1")
                    is not null => "System.Collections.Generic.IReadOnlySet`1",
            MutableCollectionKind.Set or MutableCollectionKind.Collection =>
                "System.Collections.Generic.IReadOnlyCollection`1",
            _ => null,
        };

        INamedTypeSymbol? readOnlyType = metadataName is null
            ? null
            : compilation.GetTypeByMetadataName(metadataName);

        return readOnlyType is not null && readOnlyType.Arity == typeArguments.Length
            ? readOnlyType.Construct(typeArguments)
            : null;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        INamespaceSymbol? containingNamespace = type.ContainingNamespace;
        return containingNamespace is null || containingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : containingNamespace.ToDisplayString() + "." + type.MetadataName;
    }
}
