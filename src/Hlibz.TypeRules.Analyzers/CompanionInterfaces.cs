using System.Collections.Immutable;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// TR003. A type <c>Foo</c> matched by a rule set with <c>companion_interface = true</c> must
/// implement an interface <c>IFoo</c> with the same generic arity, declared in the same namespace
/// (or containing type), that itself extends one of the rule set's matched interfaces.
/// </summary>
internal static class CompanionInterfaces
{
    /// <summary>
    /// Diagnostic property telling the code fix what to do: <see cref="MissingState"/> or
    /// <see cref="NotImplementedState"/>. Absent when there's no safe fix.
    /// </summary>
    public const string StateProperty = "CompanionState";

    /// <summary>
    /// Diagnostic property with the documentation IDs of the matched interfaces the companion has
    /// to extend, separated by <c>|</c>.
    /// </summary>
    public const string MatchedTypesProperty = "MatchedTypes";

    /// <summary>No type named <c>IFoo</c> exists yet: the fix generates it.</summary>
    public const string MissingState = "Missing";

    /// <summary><c>IFoo</c> exists and is valid, but <c>Foo</c> doesn't implement it.</summary>
    public const string NotImplementedState = "NotImplemented";

    public static string GetName(INamedTypeSymbol type)
    {
        return "I" + type.Name;
    }

    /// <summary>
    /// Finds any type named like <paramref name="type"/>'s companion next to it, interface or not.
    /// </summary>
    public static INamedTypeSymbol? FindCandidate(INamedTypeSymbol type)
    {
        INamespaceOrTypeSymbol container =
            (INamespaceOrTypeSymbol?)type.ContainingType ?? type.ContainingNamespace;

        return container.GetTypeMembers(GetName(type), type.Arity).FirstOrDefault();
    }

    public static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        List<TypeRuleSet> matching)
    {
        if (type.IsAbstract || type.IsStatic)
        {
            return;
        }

        List<TypeRuleSet> requiring = matching
            .Where(ruleSet => ruleSet.RequireCompanionInterface)
            .ToList();

        if (requiring.Count == 0)
        {
            return;
        }

        INamedTypeSymbol? candidate = FindCandidate(type);
        INamedTypeSymbol? companion = candidate?.TypeKind == TypeKind.Interface ? candidate : null;
        bool isImplemented = companion is not null && Implements(type, companion);

        List<TypeRuleSet> violated = requiring
            .Where(ruleSet => !isImplemented || !Extends(companion!, ruleSet))
            .ToList();

        if (violated.Count == 0)
        {
            return;
        }

        // The matched interfaces this type actually implements are the ones its companion has to
        // extend; a rule set can match several.
        List<INamedTypeSymbol> toExtend = violated
            .SelectMany(ruleSet => ruleSet.MatchedTypes)
            .Where(matchedType => TypeRuleSet.IsDerivedFrom(type, matchedType))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty
            .Add(
                MatchedTypesProperty,
                string.Join("|", toExtend.Select(DocumentationCommentId.CreateDeclarationId)));

        string? state = candidate is null
            ? MissingState
            : companion is not null && violated.All(ruleSet => Extends(companion, ruleSet))
                ? NotImplementedState
                : null;

        if (state is not null)
        {
            properties = properties.Add(StateProperty, state);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.CompanionInterfaceMissing,
            type.Locations[0],
            type.Locations.Skip(1),
            properties,
            type.Name,
            GetDisplayName(type),
            string.Join(
                " or ",
                toExtend.Select(matchedType => "'" + matchedType.ToDisplayString(
                    SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "'")),
            string.Join(", ", violated.Select(ruleSet => $"'{ruleSet.Name}'"))));
    }

    /// <summary><c>IFoo</c>, or <c>IFoo&lt;T&gt;</c> for a generic <c>Foo&lt;T&gt;</c>.</summary>
    public static string GetDisplayName(INamedTypeSymbol type)
    {
        return type.Arity == 0
            ? GetName(type)
            : $"{GetName(type)}<{string.Join(", ", type.TypeParameters.Select(p => p.Name))}>";
    }

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol companion)
    {
        return type.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, companion));
    }

    private static bool Extends(INamedTypeSymbol companion, TypeRuleSet ruleSet)
    {
        return ruleSet.MatchedTypes.Any(matchedType =>
            TypeRuleSet.IsDerivedFrom(companion, matchedType));
    }
}
