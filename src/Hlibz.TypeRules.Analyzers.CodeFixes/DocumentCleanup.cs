using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>Finishing steps shared by the code fixes that generate code.</summary>
internal static class DocumentCleanup
{
    /// <summary>
    /// Adds the using directives that generated type names annotated with
    /// <see cref="Simplifier.AddImportsAnnotation"/> need, shortens those names, and formats nodes
    /// annotated with <see cref="Formatter.Annotation"/>. The import adder and formatter use the
    /// workspace's default line ending rather than the file's, so the result is then normalized to
    /// <paramref name="lineEnding"/> (when the original file used one consistently).
    /// </summary>
    public static async Task<Document> CleanUpAsync(
        Document document,
        string? lineEnding,
        bool ensureFinalLineBreak,
        CancellationToken cancellationToken)
    {
        document = await ImportAdder.AddImportsAsync(
                document,
                Simplifier.AddImportsAnnotation,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        document = await Simplifier.ReduceAsync(document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        document = await Formatter.FormatAsync(
                document,
                Formatter.Annotation,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (lineEnding is null)
        {
            return document;
        }

        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        string content = text.ToString().Replace("\r\n", "\n");
        if (lineEnding != "\n")
        {
            content = content.Replace("\n", lineEnding);
        }

        if (ensureFinalLineBreak && !content.EndsWith(lineEnding, StringComparison.Ordinal))
        {
            content += lineEnding;
        }

        return document.WithText(SourceText.From(content, text.Encoding, text.ChecksumAlgorithm));
    }

    /// <summary>
    /// The line ending <paramref name="text"/> uses throughout, or null for a file that mixes them
    /// (left alone rather than normalized) or has no line breaks at all.
    /// </summary>
    public static string? GetConsistentLineEnding(SourceText text)
    {
        string? lineEnding = null;
        foreach (TextLine line in text.Lines)
        {
            if (line.EndIncludingLineBreak == line.End)
            {
                continue;
            }

            string current = text.ToString(
                TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));
            if (lineEnding is not null && lineEnding != current)
            {
                return null;
            }

            lineEnding = current;
        }

        return lineEnding;
    }
}
