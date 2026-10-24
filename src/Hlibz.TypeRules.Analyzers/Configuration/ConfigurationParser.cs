using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers.Configuration;

/// <summary>
/// Reads <c>typerules.&lt;rule set&gt;.&lt;option&gt;</c> keys and resolves their matched types
/// against a compilation. Keys arrive lowercased (the .editorconfig format is case-insensitive),
/// values keep their case.
/// </summary>
internal static class ConfigurationParser
{
    public const string KeyPrefix = "typerules.";

    public const string MatchOption = "match";
    public const string MaxAccessibilityOption = "max_accessibility";
    public const string RequireSealedOption = "require_sealed";
    public const string CompanionInterfaceOption = "companion_interface";
    public const string MaxSetterAccessibilityOption = "max_setter_accessibility";
    public const string AllowInitOption = "allow_init";
    public const string ReadOnlyCollectionsOption = "readonly_collections";
    public const string NamespacePatternOption = "namespace_pattern";
    public const string ForbidMemberTypesOption = "forbid_member_types";
    public const string AllowSelfReferencesOption = "allow_self_references";
    public const string RequireImmutableOption = "require_immutable";
    public const string EqualityOption = "equality";
    public const string MaxConstructorAccessibilityOption = "max_constructor_accessibility";

    private static readonly string[] KnownOptions =
    [
        MatchOption,
        MaxAccessibilityOption,
        RequireSealedOption,
        CompanionInterfaceOption,
        MaxSetterAccessibilityOption,
        AllowInitOption,
        ReadOnlyCollectionsOption,
        NamespacePatternOption,
        ForbidMemberTypesOption,
        AllowSelfReferencesOption,
        RequireImmutableOption,
        EqualityOption,
        MaxConstructorAccessibilityOption,
    ];

    /// <summary>Options that each add a check; a rule set needs at least one of them.</summary>
    private static readonly string[] ConstraintOptions =
    [
        MaxAccessibilityOption,
        RequireSealedOption,
        CompanionInterfaceOption,
        MaxSetterAccessibilityOption,
        ReadOnlyCollectionsOption,
        NamespacePatternOption,
        ForbidMemberTypesOption,
        RequireImmutableOption,
        EqualityOption,
        MaxConstructorAccessibilityOption,
    ];

    private static readonly char[] MatchSeparators = ['|', ','];

    public static TypeRulesConfiguration Parse(
        AnalyzerConfigOptions options,
        Compilation compilation)
    {
        ImmutableArray<string>.Builder errors = ImmutableArray.CreateBuilder<string>();
        SortedDictionary<string, Dictionary<string, string>> ruleSetOptions =
            new(StringComparer.Ordinal);

        foreach (string key in options.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal)
                || !options.TryGetValue(key, out string? value))
            {
                continue;
            }

            string rest = key.Substring(KeyPrefix.Length);
            int separator = rest.IndexOf('.');
            if (separator <= 0 || separator == rest.Length - 1)
            {
                errors.Add($"key '{key}' must have the form typerules.<rule set>.<option>");
                continue;
            }

            string ruleSetName = rest.Substring(0, separator);
            if (!IsValidRuleSetName(ruleSetName))
            {
                errors.Add(
                    $"rule set name '{ruleSetName}' may only contain letters, digits, '_' and '-'");
                continue;
            }

            if (!ruleSetOptions.TryGetValue(ruleSetName, out Dictionary<string, string>? values))
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                ruleSetOptions.Add(ruleSetName, values);
            }

            values[rest.Substring(separator + 1)] = value;
        }

        if (ruleSetOptions.Count == 0 && errors.Count == 0)
        {
            return TypeRulesConfiguration.Empty;
        }

        // A rule set another one forbids (forbid_member_types) is used as a target: it needs a
        // match, but no constraint of its own.
        HashSet<string> forbiddenTargets = new(
            ruleSetOptions.Values.SelectMany(values =>
                values.TryGetValue(ForbidMemberTypesOption, out string? names)
                    ? SplitList(names)
                    : []),
            StringComparer.Ordinal);

        ImmutableArray<TypeRuleSet>.Builder ruleSets = ImmutableArray.CreateBuilder<TypeRuleSet>();
        foreach (KeyValuePair<string, Dictionary<string, string>> entry in ruleSetOptions)
        {
            TypeRuleSet? ruleSet = ParseRuleSet(
                entry.Key,
                entry.Value,
                compilation,
                ruleSetOptions.Keys,
                forbiddenTargets.Contains(entry.Key),
                errors);
            if (ruleSet is not null)
            {
                ruleSets.Add(ruleSet);
            }
        }

        return new TypeRulesConfiguration(ruleSets.ToImmutable(), errors.ToImmutable());
    }

    private static TypeRuleSet? ParseRuleSet(
        string name,
        Dictionary<string, string> values,
        Compilation compilation,
        ICollection<string> configuredRuleSets,
        bool isForbiddenTarget,
        ImmutableArray<string>.Builder errors)
    {
        int errorCount = errors.Count;

        foreach (string option in values.Keys.OrderBy(option => option, StringComparer.Ordinal))
        {
            if (Array.IndexOf(KnownOptions, option) < 0)
            {
                errors.Add(
                    $"unknown option '{option}' in rule set '{name}' "
                    + $"(known options: {string.Join(", ", KnownOptions)})");
            }
        }

        AccessScope? maxAccessibility =
            ParseAccessibility(name, MaxAccessibilityOption, values, errors);
        AccessScope? maxSetterAccessibility =
            ParseAccessibility(name, MaxSetterAccessibilityOption, values, errors);
        bool allowInit = ParseBoolean(name, AllowInitOption, values, errors);
        bool readOnlyCollections = ParseBoolean(name, ReadOnlyCollectionsOption, values, errors);
        ImmutableArray<NamespacePattern> namespacePatterns =
            ParseNamespacePatterns(name, values, errors);
        bool requireImmutable = ParseBoolean(name, RequireImmutableOption, values, errors);
        Equality? equality = ParseEquality(name, values, errors);
        AccessScope? maxConstructorAccessibility =
            ParseAccessibility(name, MaxConstructorAccessibilityOption, values, errors);

        if (values.ContainsKey(AllowInitOption)
            && !values.ContainsKey(MaxSetterAccessibilityOption))
        {
            errors.Add(
                $"rule set '{name}' sets allow_init without max_setter_accessibility, which it "
                + "modifies");
        }

        bool requireSealed = ParseBoolean(name, RequireSealedOption, values, errors);
        bool requireCompanionInterface =
            ParseBoolean(name, CompanionInterfaceOption, values, errors);

        ImmutableArray<string> forbiddenMemberRuleSets =
            ParseForbiddenRuleSets(name, values, configuredRuleSets, errors);
        bool allowSelfReferences = !values.ContainsKey(AllowSelfReferencesOption)
            || ParseBoolean(name, AllowSelfReferencesOption, values, errors);

        if (values.ContainsKey(AllowSelfReferencesOption)
            && !values.ContainsKey(ForbidMemberTypesOption))
        {
            errors.Add(
                $"rule set '{name}' sets allow_self_references without forbid_member_types, "
                + "which it modifies");
        }

        if (!isForbiddenTarget && !ConstraintOptions.Any(values.ContainsKey))
        {
            errors.Add(
                $"rule set '{name}' sets no constraint (expected "
                + $"{string.Join(", ", ConstraintOptions)})");
        }

        ImmutableArray<INamedTypeSymbol> matchedTypes = ImmutableArray<INamedTypeSymbol>.Empty;
        if (values.TryGetValue(MatchOption, out string? matchValue))
        {
            matchedTypes = ResolveMatchedTypes(name, matchValue, compilation, errors);

            // A companion interface has to extend a matched type, and interfaces can only extend
            // interfaces.
            if (requireCompanionInterface)
            {
                foreach (INamedTypeSymbol matchedType in matchedTypes)
                {
                    if (matchedType.TypeKind != TypeKind.Interface)
                    {
                        string id = DocumentationCommentId.CreateDeclarationId(matchedType);
                        errors.Add(
                            $"rule set '{name}' sets companion_interface, so every match type "
                            + $"must be an interface, but '{id}' is not");
                    }
                }
            }
        }
        else
        {
            errors.Add($"rule set '{name}' has no match option");
        }

        // Types from assemblies this project doesn't reference resolve to nothing, and that's not
        // an error: one .editorconfig usually covers projects that can't all see every type.
        if (errors.Count > errorCount || matchedTypes.IsEmpty)
        {
            return null;
        }

        return new TypeRuleSet(
            name,
            matchedTypes,
            maxAccessibility,
            requireSealed,
            requireCompanionInterface,
            maxSetterAccessibility,
            allowInit,
            readOnlyCollections,
            namespacePatterns,
            forbiddenMemberRuleSets,
            allowSelfReferences,
            requireImmutable,
            equality,
            maxConstructorAccessibility);
    }

    private static Equality? ParseEquality(
        string ruleSetName,
        Dictionary<string, string> values,
        ImmutableArray<string>.Builder errors)
    {
        if (!values.TryGetValue(EqualityOption, out string? value))
        {
            return null;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "value":
                return Equality.Value;
            case "identity":
                return Equality.Identity;
            default:
                errors.Add(
                    $"rule set '{ruleSetName}' has an invalid equality '{value}' (expected value "
                    + "or identity)");
                return null;
        }
    }

    private static ImmutableArray<string> ParseForbiddenRuleSets(
        string ruleSetName,
        Dictionary<string, string> values,
        ICollection<string> configuredRuleSets,
        ImmutableArray<string>.Builder errors)
    {
        if (!values.TryGetValue(ForbidMemberTypesOption, out string? value))
        {
            return ImmutableArray<string>.Empty;
        }

        string[] names = SplitList(value);
        if (names.Length == 0)
        {
            errors.Add($"rule set '{ruleSetName}' has an empty forbid_member_types option");
        }

        foreach (string forbidden in names)
        {
            if (!configuredRuleSets.Contains(forbidden))
            {
                errors.Add(
                    $"rule set '{ruleSetName}' forbids member types of rule set '{forbidden}', "
                    + "which isn't configured");
            }
        }

        return names.ToImmutableArray();
    }

    /// <summary>
    /// Splits a <c>|</c>- or <c>,</c>-separated option value. Rule-set names are lowercased, like
    /// the keys they come from.
    /// </summary>
    private static string[] SplitList(string value)
    {
        return value.Split(MatchSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim().ToLowerInvariant())
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static ImmutableArray<NamespacePattern> ParseNamespacePatterns(
        string ruleSetName,
        Dictionary<string, string> values,
        ImmutableArray<string>.Builder errors)
    {
        if (!values.TryGetValue(NamespacePatternOption, out string? value))
        {
            return ImmutableArray<NamespacePattern>.Empty;
        }

        ImmutableArray<NamespacePattern>.Builder patterns =
            ImmutableArray.CreateBuilder<NamespacePattern>();

        foreach (string alternative in value.Split(MatchSeparators))
        {
            NamespacePattern? pattern = NamespacePattern.Parse(alternative);
            if (pattern is null)
            {
                errors.Add(
                    $"rule set '{ruleSetName}' has an invalid namespace_pattern '{value}' "
                    + "(expected dot-separated names, with '*' for one segment and '**' for any "
                    + "number, alternatives separated by '|')");
                return ImmutableArray<NamespacePattern>.Empty;
            }

            patterns.Add(pattern);
        }

        return patterns.ToImmutable();
    }

    private static AccessScope? ParseAccessibility(
        string ruleSetName,
        string option,
        Dictionary<string, string> values,
        ImmutableArray<string>.Builder errors)
    {
        if (!values.TryGetValue(option, out string? value))
        {
            return null;
        }

        AccessScope? scope = AccessScopes.Parse(value);
        if (scope is null)
        {
            errors.Add(
                $"rule set '{ruleSetName}' has an invalid {option} '{value}' (expected public, "
                + "protected_internal, internal, protected, private_protected or private)");
        }

        return scope;
    }

    private static bool ParseBoolean(
        string ruleSetName,
        string option,
        Dictionary<string, string> values,
        ImmutableArray<string>.Builder errors)
    {
        if (!values.TryGetValue(option, out string? value))
        {
            return false;
        }

        if (bool.TryParse(value.Trim(), out bool result))
        {
            return result;
        }

        errors.Add(
            $"rule set '{ruleSetName}' has an invalid {option} '{value}' (expected true or false)");
        return false;
    }

    private static ImmutableArray<INamedTypeSymbol> ResolveMatchedTypes(
        string ruleSetName,
        string matchValue,
        Compilation compilation,
        ImmutableArray<string>.Builder errors)
    {
        string[] entries = matchValue.Split(MatchSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .ToArray();

        if (entries.Length == 0)
        {
            errors.Add($"rule set '{ruleSetName}' has an empty match option");
            return ImmutableArray<INamedTypeSymbol>.Empty;
        }

        ImmutableArray<INamedTypeSymbol>.Builder resolved =
            ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        foreach (string entry in entries)
        {
            string id = entry.StartsWith("T:", StringComparison.Ordinal) ? entry : "T:" + entry;
            if (entry.IndexOf(':') >= 0 && !entry.StartsWith("T:", StringComparison.Ordinal))
            {
                errors.Add(
                    $"rule set '{ruleSetName}' matches '{entry}', which is not a type "
                    + "(expected T:Namespace.TypeName)");
                continue;
            }

            INamedTypeSymbol[] types = DocumentationCommentId
                .GetSymbolsForDeclarationId(id, compilation)
                .OfType<INamedTypeSymbol>()
                .ToArray();

            if (types.Length == 0)
            {
                if (ContainerExists(id, compilation))
                {
                    errors.Add(
                        $"rule set '{ruleSetName}' matches type '{id}', which was not found");
                }

                continue;
            }

            foreach (INamedTypeSymbol type in types)
            {
                if (!CanBeInheritedOrImplemented(type))
                {
                    errors.Add(
                        $"rule set '{ruleSetName}' matches '{id}', which no type can inherit from "
                        + "or implement");
                    continue;
                }

                resolved.Add(type);
            }
        }

        return resolved.ToImmutable();
    }

    /// <summary>
    /// Whether the namespace or type that would contain <paramref name="typeId"/> exists. If it
    /// does, a missing type is almost certainly a typo worth reporting; if it doesn't, the type
    /// most likely lives in an assembly this project doesn't reference.
    /// </summary>
    private static bool ContainerExists(string typeId, Compilation compilation)
    {
        string name = typeId.Substring("T:".Length);
        int lastDot = name.LastIndexOf('.');
        if (lastDot < 0)
        {
            return true;
        }

        string container = name.Substring(0, lastDot);
        return Exists("N:" + container) || Exists("T:" + container);

        bool Exists(string id)
        {
            return DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation).Any();
        }
    }

    private static bool CanBeInheritedOrImplemented(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Interface => true,
            TypeKind.Class => !type.IsSealed && !type.IsStatic,
            _ => false,
        };
    }

    private static bool IsValidRuleSetName(string name)
    {
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}
