using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// Checks every class and struct against the rule sets configured for the file it's declared in
/// (TR001-TR006), and reports configuration it couldn't apply (TR000).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeRulesAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic property holding the combined maximum <see cref="AccessScope"/> (as an integer)
    /// for TR001, so the code fix doesn't have to re-read configuration.
    /// </summary>
    internal const string MaxAccessibilityProperty = "MaxAccessibility";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Descriptors.InvalidConfiguration,
            Descriptors.AccessibilityExceedsMaximum,
            Descriptors.TypeMustBeSealed,
            Descriptors.CompanionInterfaceMissing,
            Descriptors.SetterExceedsMaximum,
            Descriptors.MutableCollectionExposed,
            Descriptors.WrongNamespace);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            CompilationAnalyzer analyzer = new(
                startContext.Compilation,
                startContext.Options.AnalyzerConfigOptionsProvider);
            startContext.RegisterSymbolAction(analyzer.AnalyzeNamedType, SymbolKind.NamedType);
        });
    }

    private sealed class CompilationAnalyzer
    {
        private readonly Compilation _compilation;
        private readonly AnalyzerConfigOptionsProvider _optionsProvider;

        // Keyed by options instance: files with the same effective configuration share one.
        private readonly ConcurrentDictionary<AnalyzerConfigOptions, TypeRulesConfiguration>
            _configurations = new();

        private readonly ConcurrentDictionary<string, bool> _reportedErrors =
            new(StringComparer.Ordinal);

        public CompilationAnalyzer(
            Compilation compilation,
            AnalyzerConfigOptionsProvider optionsProvider)
        {
            _compilation = compilation;
            _optionsProvider = optionsProvider;
        }

        public void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)
                || type.IsImplicitlyDeclared
                || type.Locations.IsEmpty
                || type.Locations[0].SourceTree is not { } syntaxTree)
            {
                return;
            }

            TypeRulesConfiguration configuration = _configurations.GetOrAdd(
                _optionsProvider.GetOptions(syntaxTree),
                options => ConfigurationParser.Parse(options, _compilation));

            ReportConfigurationErrors(context, configuration);

            List<TypeRuleSet> matching = [];
            foreach (TypeRuleSet ruleSet in configuration.RuleSets)
            {
                if (ruleSet.Matches(type))
                {
                    matching.Add(ruleSet);
                }
            }

            if (matching.Count == 0)
            {
                return;
            }

            AnalyzeAccessibility(context, type, matching);
            AnalyzeSealed(context, type, matching);
            CompanionInterfaces.Analyze(context, type, matching);
            MemberRules.Analyze(context, type, matching);
            AnalyzeNamespace(context, type, matching);
        }

        private void ReportConfigurationErrors(
            SymbolAnalysisContext context,
            TypeRulesConfiguration configuration)
        {
            foreach (string error in configuration.Errors)
            {
                if (_reportedErrors.TryAdd(error, true))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Descriptors.InvalidConfiguration, Location.None, error));
                }
            }
        }

        private static void AnalyzeAccessibility(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            List<TypeRuleSet> matching)
        {
            AccessScope effective = AccessScopes.GetEffective(type);
            AccessScope combinedMaximum = AccessScope.Public;
            List<string> violated = [];

            foreach (TypeRuleSet ruleSet in matching)
            {
                if (ruleSet.MaxAccessibility is not { } maximum)
                {
                    continue;
                }

                combinedMaximum &= maximum;
                if (!effective.IsWithin(maximum))
                {
                    violated.Add(ruleSet.Name);
                }
            }

            if (violated.Count == 0)
            {
                return;
            }

            // The fix has to satisfy every matching rule set, not just the violated ones.
            ImmutableDictionary<string, string?> properties =
                ImmutableDictionary<string, string?>.Empty.Add(
                    MaxAccessibilityProperty,
                    ((int)combinedMaximum).ToString(CultureInfo.InvariantCulture));

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.AccessibilityExceedsMaximum,
                type.Locations[0],
                type.Locations.Skip(1),
                properties,
                type.Name,
                effective.ToDisplayString(),
                FormatRuleSetNames(violated),
                combinedMaximum.ToDisplayString()));
        }

        private static void AnalyzeSealed(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            List<TypeRuleSet> matching)
        {
            if (type.TypeKind != TypeKind.Class
                || type.IsSealed
                || type.IsAbstract
                || type.IsStatic)
            {
                return;
            }

            List<string> requiring = matching
                .Where(ruleSet => ruleSet.RequireSealed)
                .Select(ruleSet => ruleSet.Name)
                .ToList();

            if (requiring.Count == 0)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.TypeMustBeSealed,
                type.Locations[0],
                type.Locations.Skip(1),
                type.Name,
                FormatRuleSetNames(requiring)));
        }

        /// <summary>
        /// One diagnostic per violated rule set: unlike accessibility maximums, two rule sets'
        /// namespace requirements can't be combined into one.
        /// </summary>
        private static void AnalyzeNamespace(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            List<TypeRuleSet> matching)
        {
            INamespaceSymbol containingNamespace = type.ContainingNamespace;
            string namespaceName = containingNamespace.IsGlobalNamespace
                ? string.Empty
                : containingNamespace.ToDisplayString();

            foreach (TypeRuleSet ruleSet in matching)
            {
                if (ruleSet.NamespacePatterns.IsEmpty
                    || ruleSet.NamespacePatterns.Any(pattern => pattern.Matches(namespaceName)))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.WrongNamespace,
                    type.Locations[0],
                    type.Locations.Skip(1),
                    type.Name,
                    namespaceName.Length == 0
                        ? "the global namespace"
                        : $"namespace '{namespaceName}'",
                    ruleSet.Name,
                    string.Join(
                        " or ",
                        ruleSet.NamespacePatterns.Select(pattern => $"'{pattern.Text}'"))));
            }
        }

        private static string FormatRuleSetNames(List<string> names)
        {
            return string.Join(", ", names.Select(name => $"'{name}'"));
        }
    }
}
