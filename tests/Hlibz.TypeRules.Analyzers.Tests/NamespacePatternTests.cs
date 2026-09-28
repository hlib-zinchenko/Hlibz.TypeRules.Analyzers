using Hlibz.TypeRules.Analyzers.Configuration;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class NamespacePatternTests
{
    [Theory]
    [InlineData("MyApp.Orders", "MyApp.Orders", true)]
    [InlineData("MyApp.Orders", "MyApp.Orders.Api", false)]
    [InlineData("MyApp.*", "MyApp.Orders", true)]
    [InlineData("MyApp.*", "MyApp", false)]
    [InlineData("MyApp.*", "MyApp.Orders.Api", false)]
    [InlineData("MyApp.**", "MyApp", true)]
    [InlineData("MyApp.**", "MyApp.Orders.Api", true)]
    [InlineData("**.Database.Configurations", "Database.Configurations", true)]
    [InlineData("**.Database.Configurations", "MyApp.Orders.Database.Configurations", true)]
    [InlineData("**.Database.Configurations", "MyApp.Database.Configurations.Old", false)]
    [InlineData("**.Endpoints.**", "MyApp.Endpoints", true)]
    [InlineData("**.Endpoints.**", "MyApp.Orders.Endpoints.Users", true)]
    [InlineData("**.Endpoints.**", "MyApp.EndpointsV2", false)]
    [InlineData("**", "", true)]
    [InlineData("MyApp.**", "", false)]
    [InlineData("MyApp.orders", "MyApp.Orders", false)]
    public void Matches_WithPatternAndNamespace_FollowsSegmentWildcards(
        string pattern,
        string namespaceName,
        bool expected)
    {
        Assert.Equal(expected, NamespacePattern.Parse(pattern)!.Matches(namespaceName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MyApp..Orders")]
    [InlineData("MyApp.Ord ers")]
    [InlineData("MyApp.***")]
    [InlineData("MyApp.Or*")]
    [InlineData("1App")]
    public void Parse_WithInvalidPattern_ReturnsNull(string pattern)
    {
        Assert.Null(NamespacePattern.Parse(pattern));
    }
}
