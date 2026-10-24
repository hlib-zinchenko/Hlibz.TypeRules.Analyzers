using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// For fixes that can break other code (changing a member's type, making it readonly, restricting
/// a constructor): checks a change speculatively before the fix is offered.
/// </summary>
internal static class SpeculativeCompilation
{
    /// <summary>
    /// Whether <paramref name="changed"/> introduces no compiler errors in the original document or
    /// in any document that declares or references one of <paramref name="symbols"/>. Compared by
    /// count per document, since the change moves spans around.
    /// </summary>
    public static async Task<bool> CompilesAsCleanlyAsync(
        Document original,
        Solution changed,
        IEnumerable<ISymbol> symbols,
        CancellationToken cancellationToken)
    {
        Solution solution = original.Project.Solution;
        HashSet<DocumentId> documentIds = [original.Id];

        foreach (ISymbol symbol in symbols)
        {
            foreach (SyntaxReference declaration in symbol.DeclaringSyntaxReferences)
            {
                if (solution.GetDocumentId(declaration.SyntaxTree) is { } documentId)
                {
                    documentIds.Add(documentId);
                }
            }

            IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
                    symbol,
                    solution,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (ReferencedSymbol referenced in references)
            {
                foreach (ReferenceLocation location in referenced.Locations)
                {
                    documentIds.Add(location.Document.Id);
                }
            }
        }

        foreach (DocumentId documentId in documentIds)
        {
            int before = await CountErrorsAsync(solution.GetDocument(documentId), cancellationToken)
                .ConfigureAwait(false);
            int after = await CountErrorsAsync(changed.GetDocument(documentId), cancellationToken)
                .ConfigureAwait(false);

            if (after > before)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<int> CountErrorsAsync(
        Document? document,
        CancellationToken cancellationToken)
    {
        if (document is null
            || await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                is not { } semanticModel)
        {
            return 0;
        }

        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
