namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class AccessScopeTests
{
    [Theory]
    [InlineData("Private", "PrivateProtected", true)]
    [InlineData("PrivateProtected", "Internal", true)]
    [InlineData("PrivateProtected", "Protected", true)]
    [InlineData("Internal", "ProtectedInternal", true)]
    [InlineData("Protected", "ProtectedInternal", true)]
    [InlineData("ProtectedInternal", "Public", true)]
    [InlineData("Protected", "Internal", false)]
    [InlineData("Internal", "Protected", false)]
    [InlineData("Public", "ProtectedInternal", false)]
    public void IsWithin_WithEveryOrderedAndUnorderedPair_FollowsCSharpVisibility(
        string scope,
        string maximum,
        bool expected)
    {
        Assert.Equal(expected, Scope(scope).IsWithin(Scope(maximum)));
    }

    [Fact]
    public void Intersection_OfInternalAndProtected_IsPrivateProtected()
    {
        Assert.Equal(AccessScope.PrivateProtected, AccessScope.Internal & AccessScope.Protected);
    }

    [Theory]
    [InlineData("public", "Public")]
    [InlineData(" Internal ", "Internal")]
    [InlineData("protected_internal", "ProtectedInternal")]
    [InlineData("protected  internal", "ProtectedInternal")]
    [InlineData("private_protected", "PrivateProtected")]
    [InlineData("private", "Private")]
    public void Parse_WithKeywordSpellings_ReturnsScope(string value, string expected)
    {
        Assert.Equal(Scope(expected), AccessScopes.Parse(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("intern")]
    [InlineData("file")]
    public void Parse_WithUnknownValue_ReturnsNull(string value)
    {
        Assert.Null(AccessScopes.Parse(value));
    }

    /// <summary>
    /// Theory data is passed as enum member names: <see cref="AccessScope"/> is internal, so it
    /// can't appear in a public test method's signature.
    /// </summary>
    private static AccessScope Scope(string name)
    {
        return Enum.Parse<AccessScope>(name);
    }
}
