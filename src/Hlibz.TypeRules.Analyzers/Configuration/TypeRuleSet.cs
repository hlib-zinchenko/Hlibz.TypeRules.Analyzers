using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers.Configuration;

/// <summary>
/// One <c>typerules.&lt;name&gt;.*</c> group from configuration, with its matched types already
/// resolved against the compilation being analyzed.
/// </summary>
internal sealed class TypeRuleSet
{
    public TypeRuleSet(
        string name,
        ImmutableArray<INamedTypeSymbol> matchedTypes,
        AccessScope? maxAccessibility,
        bool requireSealed,
        bool requireCompanionInterface,
        AccessScope? maxSetterAccessibility,
        bool allowInit,
        bool readOnlyCollections,
        ImmutableArray<NamespacePattern> namespacePatterns,
        ImmutableArray<string> forbiddenMemberRuleSets,
        bool allowSelfReferences)
    {
        Name = name;
        MatchedTypes = matchedTypes;
        MaxAccessibility = maxAccessibility;
        RequireSealed = requireSealed;
        RequireCompanionInterface = requireCompanionInterface;
        MaxSetterAccessibility = maxSetterAccessibility;
        AllowInit = allowInit;
        ReadOnlyCollections = readOnlyCollections;
        NamespacePatterns = namespacePatterns;
        ForbiddenMemberRuleSets = forbiddenMemberRuleSets;
        AllowSelfReferences = allowSelfReferences;
    }

    public string Name { get; }

    /// <summary>Base classes and interfaces (definitions, never constructed generics).</summary>
    public ImmutableArray<INamedTypeSymbol> MatchedTypes { get; }

    public AccessScope? MaxAccessibility { get; }

    public bool RequireSealed { get; }

    public bool RequireCompanionInterface { get; }

    public AccessScope? MaxSetterAccessibility { get; }

    /// <summary>
    /// Whether <c>init</c> accessors are exempt from <see cref="MaxSetterAccessibility"/>.
    /// </summary>
    public bool AllowInit { get; }

    public bool ReadOnlyCollections { get; }

    /// <summary>
    /// Alternatives: a matched type's namespace must match at least one. Empty = any namespace.
    /// </summary>
    public ImmutableArray<NamespacePattern> NamespacePatterns { get; }

    /// <summary>Names of the rule sets whose types this rule set's types may not store.</summary>
    public ImmutableArray<string> ForbiddenMemberRuleSets { get; }

    /// <summary>
    /// Whether a type may still store its own type (<c>Category.Parent</c>) when it's forbidden.
    /// </summary>
    public bool AllowSelfReferences { get; }

    /// <summary>
    /// Whether <paramref name="type"/> belongs to this rule set: it's one of the matched types
    /// itself (e.g. a field typed as the marker interface), or inherits from or implements one.
    /// </summary>
    public bool Contains(INamedTypeSymbol type)
    {
        return MatchedTypes.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default)
            || Matches(type);
    }

    /// <summary>
    /// Whether <paramref name="type"/> inherits from or implements any matched type, directly or
    /// indirectly. A matched type never matches itself. Generic matched types match every
    /// construction of them, e.g. <c>Entity`1</c> matches <c>Entity&lt;OrderId&gt;</c>.
    /// </summary>
    public bool Matches(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol matchedType in MatchedTypes)
        {
            if (IsDerivedFrom(type, matchedType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> inherits from or implements <paramref name="matchedType"/>
    /// (a definition), directly or indirectly.
    /// </summary>
    public static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol matchedType)
    {
        return matchedType.TypeKind == TypeKind.Interface
            ? Implements(type, matchedType)
            : InheritsFrom(type, matchedType);
    }

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol matchedInterface)
    {
        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    implemented.OriginalDefinition,
                    matchedInterface))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol matchedClass)
    {
        for (INamedTypeSymbol? current = type.BaseType;
             current is not null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, matchedClass))
            {
                return true;
            }
        }

        return false;
    }
}
