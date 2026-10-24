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

    /// <summary>
    /// A member as <c>Order.Lines</c>, or <c>Order.this[int]</c> for an indexer.
    /// </summary>
    public static string FormatMember(INamedTypeSymbol type, ISymbol member)
    {
        string name = member is IPropertySymbol { IsIndexer: true } indexer
            ? "this[" + string.Join(", ", indexer.Parameters.Select(parameter =>
                parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)))
                + "]"
            : member.Name;

        return $"{Format(type)}.{name}";
    }
}
