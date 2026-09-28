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
        bool requireSealed)
    {
        Name = name;
        MatchedTypes = matchedTypes;
        MaxAccessibility = maxAccessibility;
        RequireSealed = requireSealed;
    }

    public string Name { get; }

    /// <summary>Base classes and interfaces (definitions, never constructed generics).</summary>
    public ImmutableArray<INamedTypeSymbol> MatchedTypes { get; }

    public AccessScope? MaxAccessibility { get; }

    public bool RequireSealed { get; }

    /// <summary>
    /// Whether <paramref name="type"/> inherits from or implements any matched type, directly or
    /// indirectly. A matched type never matches itself. Generic matched types match every
    /// construction of them, e.g. <c>Entity`1</c> matches <c>Entity&lt;OrderId&gt;</c>.
    /// </summary>
    public bool Matches(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol matchedType in MatchedTypes)
        {
            if (matchedType.TypeKind == TypeKind.Interface
                ? Implements(type, matchedType)
                : InheritsFrom(type, matchedType))
            {
                return true;
            }
        }

        return false;
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
