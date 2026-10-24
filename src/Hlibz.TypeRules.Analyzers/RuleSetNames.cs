namespace Hlibz.TypeRules.Analyzers;

/// <summary>How diagnostic messages name the rule sets a type violates.</summary>
internal static class RuleSetNames
{
    /// <summary>
    /// <c>rule set 'a'</c>, <c>rule sets 'a' and 'b'</c> or <c>rule sets 'a', 'b' and 'c'</c>,
    /// followed by <paramref name="verb"/> (third person singular, e.g. <c>allows</c>) agreeing
    /// with the count, if given.
    /// </summary>
    public static string Format(IReadOnlyList<string> names, string? verb = null)
    {
        List<string> quoted = names.Select(name => $"'{name}'").ToList();
        string list = quoted.Count == 1
            ? quoted[0]
            : string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[quoted.Count - 1];

        string subject = (quoted.Count == 1 ? "rule set " : "rule sets ") + list;
        if (verb is null)
        {
            return subject;
        }

        return subject + " " + (quoted.Count == 1 ? verb : verb.Substring(0, verb.Length - 1));
    }
}
