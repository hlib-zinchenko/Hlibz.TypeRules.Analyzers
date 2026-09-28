namespace Hlibz.TypeRules.Analyzers.Configuration;

/// <summary>
/// A <c>namespace_pattern</c> alternative: dot-separated segments, where <c>*</c> matches exactly
/// one segment and <c>**</c> matches any number of segments, none included. Matching is ordinal,
/// like C# namespaces. <c>**.Database.Configurations</c> matches <c>Database.Configurations</c>
/// and <c>MyApp.Orders.Database.Configurations</c>; <c>**</c> alone matches every namespace,
/// the global one included.
/// </summary>
internal sealed class NamespacePattern
{
    private const string AnySegment = "*";
    private const string AnySegments = "**";

    private readonly string[] _segments;

    private NamespacePattern(string text, string[] segments)
    {
        Text = text;
        _segments = segments;
    }

    public string Text { get; }

    /// <summary>Parses one alternative, or returns null when it isn't a valid pattern.</summary>
    public static NamespacePattern? Parse(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        string[] segments = trimmed.Split('.');
        foreach (string segment in segments)
        {
            if (segment != AnySegment && segment != AnySegments && !IsIdentifier(segment))
            {
                return null;
            }
        }

        return new NamespacePattern(trimmed, segments);
    }

    /// <summary>
    /// Whether <paramref name="namespaceName"/> (e.g. <c>MyApp.Orders</c>, or an empty string for
    /// the global namespace) matches the pattern.
    /// </summary>
    public bool Matches(string namespaceName)
    {
        string[] names = namespaceName.Length == 0 ? [] : namespaceName.Split('.');
        return Matches(names, 0, 0);
    }

    private bool Matches(string[] names, int nameIndex, int segmentIndex)
    {
        while (true)
        {
            if (segmentIndex == _segments.Length)
            {
                return nameIndex == names.Length;
            }

            string segment = _segments[segmentIndex];
            if (segment == AnySegments)
            {
                // Try every split: ** takes none, one, two, ... of the remaining names.
                for (int taken = nameIndex; taken <= names.Length; taken++)
                {
                    if (Matches(names, taken, segmentIndex + 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (nameIndex == names.Length
                || (segment != AnySegment
                    && !string.Equals(segment, names[nameIndex], StringComparison.Ordinal)))
            {
                return false;
            }

            nameIndex++;
            segmentIndex++;
        }
    }

    private static bool IsIdentifier(string segment)
    {
        if (segment.Length == 0 || char.IsDigit(segment[0]))
        {
            return false;
        }

        foreach (char c in segment)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
