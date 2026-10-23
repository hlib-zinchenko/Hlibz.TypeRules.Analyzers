using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>
/// Fix All for a fix that registers every action under its rule ID as the equivalence key, so one
/// Fix All covers every diagnostic of the rule, each fixed its own way. <c>dotnet format</c>
/// starts Fix All with the key of the first diagnostic's fix, and with null when that diagnostic
/// got none; the batch fixer would then match no fix at all, so null is treated as the rule's key.
/// </summary>
internal sealed class RuleFixAllProvider : FixAllProvider
{
    private readonly string _equivalenceKey;
    private readonly FixAllProvider _inner;

    private RuleFixAllProvider(string equivalenceKey, FixAllProvider inner)
    {
        _equivalenceKey = equivalenceKey;
        _inner = inner;
    }

    /// <summary>The batch fixer, for the rule with ID <paramref name="ruleId"/>.</summary>
    public static FixAllProvider Batch(string ruleId)
    {
        return new RuleFixAllProvider(ruleId, WellKnownFixAllProviders.BatchFixer);
    }

    /// <summary>
    /// Whether a Fix All started with <paramref name="context"/>'s key is one for
    /// <paramref name="ruleId"/>'s fix: that key, or null.
    /// </summary>
    public static bool IsFor(FixAllContext context, string ruleId)
    {
        return context.CodeActionEquivalenceKey is null
            || context.CodeActionEquivalenceKey == ruleId;
    }

    /// <summary>
    /// The diagnostics in <paramref name="context"/>'s scope, for providers that apply fixes one
    /// after another instead of through the batch fixer.
    /// </summary>
    public static async Task<List<Diagnostic>> GetDiagnosticsAsync(FixAllContext context)
    {
        List<Diagnostic> diagnostics = [];
        switch (context.Scope)
        {
            case FixAllScope.Document when context.Document is not null:
                diagnostics.AddRange(await context.GetDocumentDiagnosticsAsync(context.Document)
                    .ConfigureAwait(false));
                break;
            case FixAllScope.Project:
                diagnostics.AddRange(await context.GetAllDiagnosticsAsync(context.Project)
                    .ConfigureAwait(false));
                break;
            case FixAllScope.Solution:
                foreach (Project project in context.Solution.Projects)
                {
                    diagnostics.AddRange(await context.GetAllDiagnosticsAsync(project)
                        .ConfigureAwait(false));
                }

                break;
        }

        return diagnostics;
    }

    /// <inheritdoc />
    public override IEnumerable<FixAllScope> GetSupportedFixAllScopes()
    {
        return _inner.GetSupportedFixAllScopes();
    }

    /// <inheritdoc />
    public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
    {
        if (fixAllContext.CodeActionEquivalenceKey == _equivalenceKey)
        {
            return _inner.GetFixAsync(fixAllContext);
        }

        if (fixAllContext.CodeActionEquivalenceKey is not null)
        {
            return Task.FromResult<CodeAction?>(null);
        }

        FixAllContext.DiagnosticProvider diagnostics =
            new OriginalDiagnosticProvider(fixAllContext);
        FixAllContext withKey = fixAllContext.Document is { } document
            ? new FixAllContext(
                document,
                fixAllContext.CodeFixProvider,
                fixAllContext.Scope,
                _equivalenceKey,
                fixAllContext.DiagnosticIds,
                diagnostics,
                fixAllContext.CancellationToken)
            : new FixAllContext(
                fixAllContext.Project,
                fixAllContext.CodeFixProvider,
                fixAllContext.Scope,
                _equivalenceKey,
                fixAllContext.DiagnosticIds,
                diagnostics,
                fixAllContext.CancellationToken);

        return _inner.GetFixAsync(withKey);
    }

    /// <summary>The diagnostics of the context Fix All was started with.</summary>
    private sealed class OriginalDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly FixAllContext _original;

        public OriginalDiagnosticProvider(FixAllContext original)
        {
            _original = original;
        }

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            ImmutableArray<Diagnostic> diagnostics =
                await _original.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
            return diagnostics;
        }

        public override async Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            ImmutableArray<Diagnostic> diagnostics =
                await _original.GetProjectDiagnosticsAsync(project).ConfigureAwait(false);
            return diagnostics;
        }

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            ImmutableArray<Diagnostic> diagnostics =
                await _original.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
            return diagnostics;
        }
    }
}
