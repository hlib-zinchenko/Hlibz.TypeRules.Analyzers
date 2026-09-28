using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers.CodeFixes;

/// <summary>Which accessibility levels a fix may legally write where.</summary>
internal static class Accessibilities
{
    public static bool IsProtectedLevel(AccessScope scope)
    {
        return scope is AccessScope.Protected
            or AccessScope.ProtectedInternal
            or AccessScope.PrivateProtected;
    }

    /// <summary>
    /// Structs, static classes and interfaces can't declare protected members, and a new
    /// protected member in a sealed class is a warning (CS0628).
    /// </summary>
    public static bool AllowsNewProtectedMembers(INamedTypeSymbol container)
    {
        return container.TypeKind == TypeKind.Class && !container.IsStatic && !container.IsSealed;
    }
}
