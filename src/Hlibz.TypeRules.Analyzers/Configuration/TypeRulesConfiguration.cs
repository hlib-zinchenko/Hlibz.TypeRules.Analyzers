using System.Collections.Immutable;

namespace Hlibz.TypeRules.Analyzers.Configuration;

/// <summary>
/// The rule sets that apply to one set of configuration options, plus every problem found while
/// reading them. A rule set with a problem is left out of <see cref="RuleSets"/> entirely.
/// </summary>
internal sealed class TypeRulesConfiguration
{
    public static readonly TypeRulesConfiguration Empty = new(
        ImmutableArray<TypeRuleSet>.Empty,
        ImmutableArray<string>.Empty);

    private readonly Dictionary<string, TypeRuleSet> _byName;

    public TypeRulesConfiguration(
        ImmutableArray<TypeRuleSet> ruleSets,
        ImmutableArray<string> errors)
    {
        RuleSets = ruleSets;
        Errors = errors;
        _byName = ruleSets.ToDictionary(ruleSet => ruleSet.Name, StringComparer.Ordinal);
    }

    public ImmutableArray<TypeRuleSet> RuleSets { get; }

    public ImmutableArray<string> Errors { get; }

    /// <summary>
    /// The valid rule set named <paramref name="name"/>, or null when it's invalid or matches no
    /// type this project can see.
    /// </summary>
    public TypeRuleSet? Find(string name)
    {
        return _byName.TryGetValue(name, out TypeRuleSet? ruleSet) ? ruleSet : null;
    }
}
