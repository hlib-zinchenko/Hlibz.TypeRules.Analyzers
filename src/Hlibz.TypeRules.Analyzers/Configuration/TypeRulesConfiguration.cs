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

    public TypeRulesConfiguration(
        ImmutableArray<TypeRuleSet> ruleSets,
        ImmutableArray<string> errors)
    {
        RuleSets = ruleSets;
        Errors = errors;
    }

    public ImmutableArray<TypeRuleSet> RuleSets { get; }

    public ImmutableArray<string> Errors { get; }
}
