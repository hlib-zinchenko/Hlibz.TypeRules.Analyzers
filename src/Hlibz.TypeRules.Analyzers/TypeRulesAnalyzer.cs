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

        private readonly Lazy<Dictionary<string, string>> _errorFiles;

        public CompilationAnalyzer(
            Compilation compilation,
            AnalyzerConfigOptionsProvider optionsProvider)
        {
            _compilation = compilation;
            _optionsProvider = optionsProvider;
            _errorFiles = new Lazy<Dictionary<string, string>>(FindErrorFiles);
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

            TypeRulesConfiguration configuration = GetConfiguration(syntaxTree);

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

        private TypeRulesConfiguration GetConfiguration(SyntaxTree syntaxTree)
        {
            return _configurations.GetOrAdd(
                _optionsProvider.GetOptions(syntaxTree),
                options => ConfigurationParser.Parse(options, _compilation));
        }

        private void ReportConfigurationErrors(
            SymbolAnalysisContext context,
            TypeRulesConfiguration configuration)
        {
            foreach (string error in configuration.Errors)
            {
                if (_reportedErrors.TryAdd(error, true))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.InvalidConfiguration,
                        Location.None,
                        error,
                        _errorFiles.Value.TryGetValue(error, out string? file) ? file : "?"));
                }
            }
        }

        /// <summary>
        /// For each configuration error, the first file (by path) whose options have it, relative
        /// to the project directory. Diagnostics can't point into .editorconfig, so this says which
        /// configuration to look at. Found over every file rather than whichever one was analyzed
        /// first, so the message is the same on every build.
        /// </summary>
        private Dictionary<string, string> FindErrorFiles()
        {
            _optionsProvider.GlobalOptions.TryGetValue(
                "build_property.projectdir",
                out string? projectDirectory);

            Dictionary<string, string> errorFiles = new(StringComparer.Ordinal);
            foreach (SyntaxTree syntaxTree in _compilation.SyntaxTrees
                .OrderBy(tree => tree.FilePath, StringComparer.Ordinal))
            {
                foreach (string error in GetConfiguration(syntaxTree).Errors)
                {
                    if (!errorFiles.ContainsKey(error))
                    {
                        errorFiles.Add(error, GetDisplayPath(syntaxTree.FilePath, projectDirectory));
                    }
                }
            }

            return errorFiles;
        }

        private static string GetDisplayPath(string path, string? projectDirectory)
        {
            if (!string.IsNullOrEmpty(projectDirectory)
                && path.StartsWith(projectDirectory, StringComparison.Ordinal))
            {
                path = path.Substring(projectDirectory!.Length);
            }

            return path.Replace('\\', '/');
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
                TypeNames.Format(type),
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
                TypeNames.Format(type),
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
                    TypeNames.Format(type),
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
                List<TypeRuleSet> requiring = matching
                    .Where(ruleSet => ruleSet.Equality == required)
                    .ToList();

                if (requiring.Count == 0)
                {
                    continue;
                }

                string? problem = required == Equality.Value
                    ? GetValueEqualityProblem(type)
                    : GetIdentityEqualityProblem(type, requiring);

                if (problem is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.WrongEquality,
                    type.Locations[0],
                    type.Locations.Skip(1),
                    TypeNames.Format(type),
                    problem,
                    FormatRuleSetNames(requiring.Select(ruleSet => ruleSet.Name).ToList()),
                    required == Equality.Value ? "value" : "identity"));
            }
        }

        private static string? GetValueEqualityProblem(INamedTypeSymbol type)
        {
            return type.IsRecord || type.TypeKind == TypeKind.Struct || OverridesEquals(type)
                ? null
                : "compares by reference";
        }

        /// <summary>
        /// Records and structs compare their state. A type matched through a class (an
        /// <c>Entity&lt;TId&gt;</c> base) also can't redefine equality itself: the matched class
        /// decides how entities compare. Matched only through interfaces, an <c>Equals</c>
        /// override may well compare ids, so it isn't reported.
        /// </summary>
        private static string? GetIdentityEqualityProblem(
            INamedTypeSymbol type,
            List<TypeRuleSet> requiring)
        {
            string? problem = type switch
            {
                { IsRecord: true, TypeKind: TypeKind.Struct } => "is a record struct",
                { IsRecord: true } => "is a record",
                { TypeKind: TypeKind.Struct } => "is a struct",
                _ => null,
            };

            if (problem is not null
                || !requiring.Any(ruleSet => ruleSet.MatchedTypes.Any(matchedType =>
                    matchedType.TypeKind == TypeKind.Class
                    && TypeRuleSet.IsDerivedFrom(type, matchedType))))
            {
                return problem;
            }

            if (DeclaresEqualsOverride(type))
            {
                return "overrides Equals(object)";
            }

            INamedTypeSymbol? equatable = type.Interfaces.FirstOrDefault(implemented =>
                implemented.Name == "IEquatable"
                && implemented.Arity == 1
                && implemented.ContainingNamespace.ToDisplayString() == "System");

            return equatable is null
                ? null
                : "implements " + equatable.ToDisplayString(
                    SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        }

        private static bool DeclaresEqualsOverride(INamedTypeSymbol type)
        {
            return type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>().Any(method =>
                method.IsOverride
                && method.Parameters.Length == 1
                && method.Parameters[0].Type.SpecialType == SpecialType.System_Object);
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
                if (DeclaresEqualsOverride(current))
                {
                    return true;
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
