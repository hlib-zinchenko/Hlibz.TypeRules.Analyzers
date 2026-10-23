using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>How diagnostic messages name a type.</summary>
internal static class TypeNames
{
    private static readonly SymbolDisplayFormat NameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    /// The type's name with its type parameters, e.g. <c>EntityConfiguration&lt;TEntity&gt;</c>,
    /// without namespace or containing types.
    /// </summary>
    public static string Format(INamedTypeSymbol type)
    {
        return type.ToDisplayString(NameFormat);
    }
}
