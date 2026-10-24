using System.Collections.Immutable;

using Hlibz.TypeRules.Analyzers.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// TR303: a matched type can't change after construction. Checks the instance state the type
/// declares itself: fields must be readonly, properties can't have set accessors (init runs only
/// during construction, so it's fine), nothing stored may be a mutable collection, and a struct
/// must be a readonly struct. Static members aren't instance state and are skipped, and so are
/// overrides and explicit interface implementations, as in <see cref="MemberRules"/>.
/// </summary>
internal static class Immutability
{
    /// <summary>
    /// Diagnostic property saying which of the four problems a TR303 diagnostic is, so the code
    /// fix doesn't have to work it out again.
    /// </summary>
    public const string ProblemProperty = "Problem";

    public const string MutableField = "Field";
    public const string SetAccessor = "Setter";
    public const string MutableCollection = "Collection";
    public const string MutableStruct = "Struct";

    public static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        List<TypeRuleSet> matching)
    {
        List<string> requiring = matching
            .Where(ruleSet => ruleSet.RequireImmutable)
            .Select(ruleSet => $"'{ruleSet.Name}'")
            .ToList();

        if (requiring.Count == 0)
        {
            return;
        }

        string ruleSetNames = string.Join(", ", requiring);

        if (type.TypeKind == TypeKind.Struct && !type.IsReadOnly)
        {
            Report(
                context,
                MutableStruct,
                type.Locations[0],
                type.Locations.Skip(1),
                type.Name,
                "is a struct that isn't readonly",
                ruleSetNames);
        }

        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            switch (member)
            {
                case IFieldSymbol field when !field.IsConst:
                    AnalyzeField(context, type, field, ruleSetNames);
                    break;

                case IPropertySymbol
                    {
                        IsImplicitlyDeclared: false,
                        IsOverride: false,
                        ExplicitInterfaceImplementations.IsEmpty: true,
                        SetMethod: { IsInitOnly: false } setter,
                    } property when !property.Locations.IsEmpty:
                    Report(
                        context,
                        SetAccessor,
                        setter.Locations.FirstOrDefault() ?? property.Locations[0],
                        [],
                        $"{type.Name}.{property.Name}",
                        "has a set accessor",
                        ruleSetNames);
                    break;
            }
        }
    }

    /// <summary>
    /// Explicit fields must be readonly. Auto-property backing fields are readonly exactly when
    /// the property has no set accessor, which is reported on the property, so they're only
    /// checked for what they hold.
    /// </summary>
    private static void AnalyzeField(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        IFieldSymbol field,
        string ruleSetNames)
    {
        ISymbol? stored = field switch
        {
            { IsImplicitlyDeclared: true, AssociatedSymbol: IPropertySymbol property } => property,
            { IsImplicitlyDeclared: false } => field,
            _ => null,
        };

        if (stored is null || stored.Locations.IsEmpty)
        {
            return;
        }

        string name = $"{type.Name}.{stored.Name}";

        if (!field.IsImplicitlyDeclared && !field.IsReadOnly)
        {
            Report(
                context,
                MutableField,
                field.Locations[0],
                [],
                name,
                "is a field that isn't readonly",
                ruleSetNames);
        }

        if (MutableCollections.Classify(field.Type, out _) is not null)
        {
            string typeName =
                field.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            Report(
                context,
                MutableCollection,
                stored.Locations[0],
                [],
                name,
                $"stores the mutable collection type '{typeName}'",
                ruleSetNames);
        }
    }

    private static void Report(
        SymbolAnalysisContext context,
        string problem,
        Location location,
        IEnumerable<Location> additionalLocations,
        string name,
        string description,
        string ruleSetNames)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TypeMustBeImmutable,
            location,
            additionalLocations,
            ImmutableDictionary<string, string?>.Empty.Add(ProblemProperty, problem),
            name,
            description,
            ruleSetNames));
    }
}
