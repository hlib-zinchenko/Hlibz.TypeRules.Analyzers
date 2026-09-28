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

    private static readonly string[] KnownOptions =
    [
        MatchOption,
        MaxAccessibilityOption,
        RequireSealedOption,
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

        ImmutableArray<TypeRuleSet>.Builder ruleSets = ImmutableArray.CreateBuilder<TypeRuleSet>();
        foreach (KeyValuePair<string, Dictionary<string, string>> entry in ruleSetOptions)
        {
            TypeRuleSet? ruleSet = ParseRuleSet(entry.Key, entry.Value, compilation, errors);
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

        AccessScope? maxAccessibility = null;
        if (values.TryGetValue(MaxAccessibilityOption, out string? maxAccessibilityValue))
        {
            maxAccessibility = AccessScopes.Parse(maxAccessibilityValue);
            if (maxAccessibility is null)
            {
                errors.Add(
                    $"rule set '{name}' has an invalid max_accessibility '{maxAccessibilityValue}' "
                    + "(expected public, protected_internal, internal, protected, "
                    + "private_protected or private)");
            }
        }

        bool requireSealed = false;
        if (values.TryGetValue(RequireSealedOption, out string? requireSealedValue)
            && !bool.TryParse(requireSealedValue.Trim(), out requireSealed))
        {
            errors.Add(
                $"rule set '{name}' has an invalid require_sealed '{requireSealedValue}' "
                + "(expected true or false)");
        }

        if (!values.ContainsKey(MaxAccessibilityOption) && !values.ContainsKey(RequireSealedOption))
        {
            errors.Add(
                $"rule set '{name}' sets no constraint (expected max_accessibility or "
                + "require_sealed)");
        }

        ImmutableArray<INamedTypeSymbol> matchedTypes = ImmutableArray<INamedTypeSymbol>.Empty;
        if (values.TryGetValue(MatchOption, out string? matchValue))
        {
            matchedTypes = ResolveMatchedTypes(name, matchValue, compilation, errors);
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

        return new TypeRuleSet(name, matchedTypes, maxAccessibility, requireSealed);
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
