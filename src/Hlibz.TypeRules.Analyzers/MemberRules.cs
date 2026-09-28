using System.Collections.Immutable;
using System.Globalization;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// TR004 and TR005: checks on the properties and fields a matched type declares itself. Inherited
/// members are checked on the type that declares them (if it's matched), overrides and explicit
/// interface implementations not at all, since their shape is dictated by what they implement.
/// </summary>
internal static class MemberRules
{
    /// <summary>
    /// Diagnostic property holding the combined maximum setter <see cref="AccessScope"/> (as an
    /// integer) for TR004.
    /// </summary>
    public const string MaxSetterAccessibilityProperty = "MaxSetterAccessibility";

    public static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        List<TypeRuleSet> matching)
    {
        List<TypeRuleSet> setterRuleSets = matching
            .Where(ruleSet => ruleSet.MaxSetterAccessibility is not null)
            .ToList();
        List<TypeRuleSet> collectionRuleSets = matching
            .Where(ruleSet => ruleSet.ReadOnlyCollections)
            .ToList();

        if (setterRuleSets.Count == 0 && collectionRuleSets.Count == 0)
        {
            return;
        }

        AccessScope typeScope = AccessScopes.GetEffective(type);

        foreach (ISymbol member in type.GetMembers())
        {
            // Members in a generated partial part need no check here: with generated-code analysis
            // off, the analyzer driver drops diagnostics located in generated code.
            if (member.IsImplicitlyDeclared || member.Locations.IsEmpty)
            {
                continue;
            }

            switch (member)
            {
                case IPropertySymbol property
                    when !property.IsOverride && property.ExplicitInterfaceImplementations.IsEmpty:
                    AnalyzeSetter(context, type, property, typeScope, setterRuleSets);
                    AnalyzeCollection(
                        context,
                        type,
                        property,
                        property.Type,
                        typeScope,
                        collectionRuleSets);
                    break;

                case IFieldSymbol field when !field.IsConst:
                    AnalyzeCollection(
                        context,
                        type,
                        field,
                        field.Type,
                        typeScope,
                        collectionRuleSets);
                    break;
            }
        }
    }

    private static void AnalyzeSetter(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        IPropertySymbol property,
        AccessScope typeScope,
        List<TypeRuleSet> ruleSets)
    {
        if (property.SetMethod is not { } setter || ruleSets.Count == 0)
        {
            return;
        }

        bool isInit = setter.IsInitOnly;
        AccessScope setterScope =
            AccessScopes.FromAccessibility(setter.DeclaredAccessibility) & typeScope;
        AccessScope combinedMaximum = AccessScope.Public;
        List<string> violated = [];

        foreach (TypeRuleSet ruleSet in ruleSets)
        {
            if (isInit && ruleSet.AllowInit)
            {
                continue;
            }

            AccessScope maximum = ruleSet.MaxSetterAccessibility!.Value;
            combinedMaximum &= maximum;
            if (!setterScope.IsWithin(maximum))
            {
                violated.Add(ruleSet.Name);
            }
        }

        if (violated.Count == 0)
        {
            return;
        }

        ImmutableDictionary<string, string?> properties =
            ImmutableDictionary<string, string?>.Empty.Add(
                MaxSetterAccessibilityProperty,
                ((int)combinedMaximum).ToString(CultureInfo.InvariantCulture));

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.SetterExceedsMaximum,
            setter.Locations.FirstOrDefault() ?? property.Locations[0],
            properties,
            isInit ? "init" : "set",
            $"{type.Name}.{property.Name}",
            setterScope.ToDisplayString(),
            FormatRuleSetNames(violated),
            combinedMaximum.ToDisplayString()));
    }

    private static void AnalyzeCollection(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        ISymbol member,
        ITypeSymbol memberType,
        AccessScope typeScope,
        List<TypeRuleSet> ruleSets)
    {
        if (ruleSets.Count == 0
            || (AccessScopes.FromAccessibility(member.DeclaredAccessibility) & typeScope)
                == AccessScope.Private
            || MutableCollections.Classify(memberType, out _) is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.MutableCollectionExposed,
            member.Locations[0],
            $"{type.Name}.{member.Name}",
            memberType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            FormatRuleSetNames(ruleSets.Select(ruleSet => ruleSet.Name).ToList())));
    }

    private static string FormatRuleSetNames(List<string> names)
    {
        return string.Join(", ", names.Select(name => $"'{name}'"));
    }

    /// <summary>
    /// TR007: the state a matched type stores (explicit fields of any accessibility, and
    /// auto-properties through their backing fields) must not hold a type of a rule set it
    /// forbids. Computed properties are skipped: they store nothing, and whatever they read from
    /// is a field that's checked itself.
    /// </summary>
    public static void AnalyzeReferences(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        List<TypeRuleSet> matching,
        TypeRulesConfiguration configuration)
    {
        List<(TypeRuleSet Owner, TypeRuleSet Forbidden)> forbidden = [];
        foreach (TypeRuleSet ruleSet in matching)
        {
            foreach (string name in ruleSet.ForbiddenMemberRuleSets)
            {
                if (configuration.Find(name) is { } forbiddenRuleSet)
                {
                    forbidden.Add((ruleSet, forbiddenRuleSet));
                }
            }
        }

        if (forbidden.Count == 0)
        {
            return;
        }

        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
        {
            ISymbol? stored = field switch
            {
                { IsConst: true } => null,
                { IsImplicitlyDeclared: true, AssociatedSymbol: IPropertySymbol property } =>
                    property,
                { IsImplicitlyDeclared: false } => field,
                _ => null,
            };

            if (stored is null || stored.Locations.IsEmpty)
            {
                continue;
            }

            foreach ((TypeRuleSet owner, TypeRuleSet forbiddenRuleSet) in forbidden)
            {
                INamedTypeSymbol? held = FindHeldType(
                    field.Type,
                    candidate => forbiddenRuleSet.Contains(candidate)
                        && !(owner.AllowSelfReferences
                            && SymbolEqualityComparer.Default.Equals(
                                candidate.OriginalDefinition,
                                type.OriginalDefinition)));

                if (held is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ForbiddenMemberType,
                    stored.Locations[0],
                    $"{type.Name}.{stored.Name}",
                    held.WithNullableAnnotation(NullableAnnotation.None)
                        .ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    forbiddenRuleSet.Name,
                    owner.Name));
                break;
            }
        }
    }

    /// <summary>
    /// The first type in <paramref name="type"/> that <paramref name="isForbidden"/> accepts: the
    /// type itself, an array's element type, or any type argument, recursively, so
    /// <c>List&lt;Order&gt;</c>, <c>Dictionary&lt;Guid, Order&gt;</c>, <c>Order?</c>,
    /// <c>Lazy&lt;Order&gt;</c> and <c>(Order, int)</c> all hold an <c>Order</c>.
    /// </summary>
    private static INamedTypeSymbol? FindHeldType(
        ITypeSymbol type,
        Func<INamedTypeSymbol, bool> isForbidden)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindHeldType(array.ElementType, isForbidden);

            case INamedTypeSymbol named:
                if (isForbidden(named))
                {
                    return named;
                }

                foreach (ITypeSymbol typeArgument in named.TypeArguments)
                {
                    if (FindHeldType(typeArgument, isForbidden) is { } held)
                    {
                        return held;
                    }
                }

                return null;

            default:
                return null;
        }
    }
}
