using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// Checks every class and struct against the rule sets configured for the file it's declared in
/// (TR1xx-TR3xx), and reports configuration it couldn't apply (TR000).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeRulesAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic property holding the combined maximum <see cref="AccessScope"/> (as an integer)
    /// for TR101, so the code fix doesn't have to re-read configuration.
    /// </summary>
    internal const string MaxAccessibilityProperty = "MaxAccessibility";

    /// <summary>
    /// Diagnostic property holding the combined maximum constructor <see cref="AccessScope"/> (as
    /// an integer) for TR201.
    /// </summary>
    internal const string MaxConstructorAccessibilityProperty = "MaxConstructorAccessibility";

    /// <summary>
    /// A constructor as <c>Money(decimal, string)</c>, without its containing type.
    /// </summary>
    private static readonly SymbolDisplayFormat ConstructorFormat = new(
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Descriptors.InvalidConfiguration,
            Descriptors.AccessibilityExceedsMaximum,
            Descriptors.TypeMustBeSealed,
            Descriptors.WrongNamespace,
            Descriptors.CompanionInterfaceMissing,
            Descriptors.ConstructorExceedsMaximum,
            Descriptors.WrongEquality,
            Descriptors.SetterExceedsMaximum,
            Descriptors.MutableCollectionExposed,
            Descriptors.TypeMustBeImmutable,
            Descriptors.ForbiddenMemberType);

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

            // TR1xx: the type's declaration.
            AnalyzeAccessibility(context, type, matching);
            AnalyzeSealed(context, type, matching);
            AnalyzeNamespace(context, type, matching);
            CompanionInterfaces.Analyze(context, type, matching);

            // TR2xx: construction and equality.
            AnalyzeConstructors(context, type, matching);
            AnalyzeEquality(context, type, matching);

            // TR3xx: stored state.
            MemberRules.Analyze(context, type, matching);
            Immutability.Analyze(context, type, matching);
            MemberRules.AnalyzeReferences(context, type, matching, configuration);
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

        /// <summary>
        /// One diagnostic per required equality: a type matched by rule sets requiring both can't
        /// satisfy them, and each deserves its own message.
        /// </summary>
        private static void AnalyzeEquality(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            List<TypeRuleSet> matching)
        {
            foreach (Equality required in new[] { Equality.Value, Equality.Identity })
            {
                List<string> requiring = matching
                    .Where(ruleSet => ruleSet.Equality == required)
                    .Select(ruleSet => ruleSet.Name)
                    .ToList();

                string? problem = required == Equality.Value
                    ? GetValueEqualityProblem(type)
                    : GetIdentityEqualityProblem(type);

                if (requiring.Count == 0 || problem is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.WrongEquality,
                    type.Locations[0],
                    type.Locations.Skip(1),
                    type.Name,
                    problem,
                    FormatRuleSetNames(requiring),
                    required == Equality.Value ? "value" : "identity"));
            }
        }

        private static string? GetValueEqualityProblem(INamedTypeSymbol type)
        {
            return type.IsRecord || type.TypeKind == TypeKind.Struct || OverridesEquals(type)
                ? null
                : "compares by reference";
        }

        private static string? GetIdentityEqualityProblem(INamedTypeSymbol type)
        {
            return type switch
            {
                { IsRecord: true, TypeKind: TypeKind.Struct } => "is a record struct",
                { IsRecord: true } => "is a record",
                { TypeKind: TypeKind.Struct } => "is a struct",
                _ => null,
            };
        }

        /// <summary>
        /// Whether the type or a base class below <see cref="object"/> overrides
        /// <c>Equals(object)</c>, as a <c>ValueObject</c> base class comparing equality components
        /// does.
        /// </summary>
        private static bool OverridesEquals(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type;
                 current is not null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (IMethodSymbol method
                    in current.GetMembers(nameof(Equals)).OfType<IMethodSymbol>())
                {
                    if (method.IsOverride
                        && method.Parameters.Length == 1
                        && method.Parameters[0].Type.SpecialType == SpecialType.System_Object)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Abstract classes are skipped: nothing can create one, and their constructors exist for
        /// derived types to call. So are the implicit parameterless constructor of a struct, which
        /// can't be restricted, and a record's implicit copy constructor, which only
        /// <c>with</c> expressions call on an existing instance.
        /// </summary>
        private static void AnalyzeConstructors(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            List<TypeRuleSet> matching)
        {
            if (type.IsAbstract || type.IsStatic)
            {
                return;
            }

            List<TypeRuleSet> ruleSets = matching
                .Where(ruleSet => ruleSet.MaxConstructorAccessibility is not null)
                .ToList();

            if (ruleSets.Count == 0)
            {
                return;
            }

            AccessScope typeScope = AccessScopes.GetEffective(type);
            AccessScope combinedMaximum = AccessScope.Public;
            foreach (TypeRuleSet ruleSet in ruleSets)
            {
                combinedMaximum &= ruleSet.MaxConstructorAccessibility!.Value;
            }

            // The fix has to satisfy every matching rule set, not just the violated ones.
            ImmutableDictionary<string, string?> properties =
                ImmutableDictionary<string, string?>.Empty.Add(
                    MaxConstructorAccessibilityProperty,
                    ((int)combinedMaximum).ToString(CultureInfo.InvariantCulture));

            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (constructor.IsImplicitlyDeclared
                    && (type.TypeKind == TypeKind.Struct || IsCopyConstructor(type, constructor)))
                {
                    continue;
                }

                AccessScope scope =
                    AccessScopes.FromAccessibility(constructor.DeclaredAccessibility) & typeScope;
                List<string> violated = ruleSets
                    .Where(ruleSet => !scope.IsWithin(ruleSet.MaxConstructorAccessibility!.Value))
                    .Select(ruleSet => ruleSet.Name)
                    .ToList();

                if (violated.Count == 0)
                {
                    continue;
                }

                // The implicit default constructor has no location of its own.
                bool isImplicit = constructor.IsImplicitlyDeclared || constructor.Locations.IsEmpty;
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ConstructorExceedsMaximum,
                    isImplicit ? type.Locations[0] : constructor.Locations[0],
                    isImplicit ? type.Locations.Skip(1) : [],
                    properties,
                    constructor.ToDisplayString(ConstructorFormat),
                    scope.ToDisplayString(),
                    FormatRuleSetNames(violated),
                    combinedMaximum.ToDisplayString()));
            }
        }

        private static bool IsCopyConstructor(INamedTypeSymbol type, IMethodSymbol constructor)
        {
            return type.IsRecord
                && constructor.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);
        }

        private static string FormatRuleSetNames(List<string> names)
        {
            return string.Join(", ", names.Select(name => $"'{name}'"));
        }
    }
}
