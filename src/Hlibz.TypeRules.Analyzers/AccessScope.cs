using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// An accessibility level expressed as the set of places a symbol can be seen from. Accessibility
/// levels are only partially ordered (<c>protected</c> and <c>internal</c> each allow something
/// the other doesn't), so "exceeds the maximum" is a subset check on these flags, not a
/// comparison of ranks. The six C# levels are closed under intersection, which is how the
/// effective accessibility of a nested type is computed.
/// </summary>
[Flags]
internal enum AccessScope
{
    /// <summary>Only the containing type (<c>private</c>, or a file-local type).</summary>
    None = 0,

    /// <summary>Derived types declared in the same assembly.</summary>
    DerivedTypesInAssembly = 1,

    /// <summary>Any code in the same assembly.</summary>
    ContainingAssembly = 2,

    /// <summary>Derived types declared in other assemblies.</summary>
    DerivedTypesOutsideAssembly = 4,

    /// <summary>Any code anywhere.</summary>
    Everyone = 8,

    Private = None,
    PrivateProtected = DerivedTypesInAssembly,
    Internal = DerivedTypesInAssembly | ContainingAssembly,
    Protected = DerivedTypesInAssembly | DerivedTypesOutsideAssembly,
    ProtectedInternal = Internal | Protected,
    Public = ProtectedInternal | Everyone,
}

internal static class AccessScopes
{
    /// <summary>
    /// Every level from most to least permissive, used to pick the most permissive level that
    /// still fits within a maximum.
    /// </summary>
    public static readonly AccessScope[] MostToLeastPermissive =
    [
        AccessScope.Public,
        AccessScope.ProtectedInternal,
        AccessScope.Internal,
        AccessScope.Protected,
        AccessScope.PrivateProtected,
        AccessScope.Private,
    ];

    public static bool IsWithin(this AccessScope scope, AccessScope maximum)
    {
        return (scope & ~maximum) == AccessScope.None;
    }

    public static AccessScope FromAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => AccessScope.Public,
            Accessibility.ProtectedOrInternal => AccessScope.ProtectedInternal,
            Accessibility.Internal => AccessScope.Internal,
            Accessibility.Protected => AccessScope.Protected,
            Accessibility.ProtectedAndInternal => AccessScope.PrivateProtected,
            _ => AccessScope.Private,
        };
    }

    public static Accessibility ToAccessibility(this AccessScope scope)
    {
        return scope switch
        {
            AccessScope.Public => Accessibility.Public,
            AccessScope.ProtectedInternal => Accessibility.ProtectedOrInternal,
            AccessScope.Internal => Accessibility.Internal,
            AccessScope.Protected => Accessibility.Protected,
            AccessScope.PrivateProtected => Accessibility.ProtectedAndInternal,
            _ => Accessibility.Private,
        };
    }

    /// <summary>
    /// Where the type can actually be seen from: its declared accessibility narrowed by every
    /// containing type's. A <c>public</c> class nested in an <c>internal</c> one is internal.
    /// </summary>
    public static AccessScope GetEffective(INamedTypeSymbol type)
    {
        AccessScope scope = AccessScope.Public;
        for (INamedTypeSymbol? current = type;
             current is not null;
             current = current.ContainingType)
        {
            scope &= current.IsFileLocal
                ? AccessScope.None
                : FromAccessibility(current.DeclaredAccessibility);
        }

        return scope;
    }

    public static string ToDisplayString(this AccessScope scope)
    {
        return scope switch
        {
            AccessScope.Public => "public",
            AccessScope.ProtectedInternal => "protected internal",
            AccessScope.Internal => "internal",
            AccessScope.Protected => "protected",
            AccessScope.PrivateProtected => "private protected",
            _ => "private",
        };
    }

    /// <summary>
    /// Parses a <c>max_accessibility</c> value. Accepts the C# keywords, with either a space or an
    /// underscore between the two words of <c>protected internal</c> and <c>private protected</c>.
    /// </summary>
    public static AccessScope? Parse(string value)
    {
        string normalized = string.Join(
            " ",
            value.Trim().ToLowerInvariant().Replace('_', ' ')
                .Split([' '], StringSplitOptions.RemoveEmptyEntries));

        return normalized switch
        {
            "public" => AccessScope.Public,
            "protected internal" or "internal protected" => AccessScope.ProtectedInternal,
            "internal" => AccessScope.Internal,
            "protected" => AccessScope.Protected,
            "private protected" or "protected private" => AccessScope.PrivateProtected,
            "private" => AccessScope.Private,
            _ => null,
        };
    }
}
