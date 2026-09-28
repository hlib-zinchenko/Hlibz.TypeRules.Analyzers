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
}
