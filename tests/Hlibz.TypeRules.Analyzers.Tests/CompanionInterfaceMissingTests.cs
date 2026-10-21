using Hlibz.TypeRules.Analyzers.CodeFixes;

using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

using static Hlibz.TypeRules.Analyzers.Tests.Verifier;

namespace Hlibz.TypeRules.Analyzers.Tests;

public sealed class CompanionInterfaceMissingTests
{
    private const string Handlers = """
        typerules.handlers.match = T:App.Contracts.IRequestHandler
        typerules.handlers.companion_interface = true
        """;

    /// <summary>TR104 for <c>GetUserHandler</c>, marked <c>{|#0:...|}</c>.</summary>
    private static DiagnosticResult GetUserHandlerDiagnostic()
    {
        return Diagnostic(Descriptors.CompanionInterfaceMissing)
            .WithLocation(0)
            .WithArguments("GetUserHandler", "IGetUserHandler", "'IRequestHandler'", "'handlers'");
    }

    [Fact]
    public async Task Analyze_WithCompanionImplemented_ReportsNothing()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            internal sealed class GetUserHandler : IGetUserHandler { }
            """;

        await VerifyAnalyzerAsync(Handlers, source);
    }

    [Fact]
    public async Task Analyze_WithoutCompanion_ReportsTR104()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            internal sealed class {|#0:GetUserHandler|} : IRequestHandler<int, string> { }
            """;

        await VerifyAnalyzerAsync(
            Handlers,
            source,
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Analyze_WithCompanionInAnotherNamespace_ReportsTR104()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users
            {
                internal sealed class {|#0:GetUserHandler|} : Other.IGetUserHandler { }
            }

            namespace App.Users.Other
            {
                public interface IGetUserHandler : IRequestHandler<int, string>;
            }
            """;

        await VerifyAnalyzerAsync(
            Handlers,
            source,
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Analyze_WithAbstractType_ReportsNothing()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            internal abstract class HandlerBase : IRequestHandler<int, string> { }
            """;

        await VerifyAnalyzerAsync(Handlers, source);
    }

    [Fact]
    public async Task Fix_WithMissingCompanion_GeneratesInterfaceFileAndImplementsIt()
    {
        const string source = """
            using System;
            using App.Contracts;

            namespace App.Users;

            internal sealed class {|#0:GetUserHandler|} : IRequestHandler<int, string>, IDisposable
            {
                public void Dispose() { }
            }
            """;
        const string fixedSource = """
            using System;
            using App.Contracts;

            namespace App.Users;

            internal sealed class GetUserHandler : IGetUserHandler, IDisposable
            {
                public void Dispose() { }
            }
            """;
        const string companion = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            [source],
            [fixedSource],
            [("IGetUserHandler.cs", companion)],
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task FixAll_WithSeveralHandlers_GeneratesEveryInterfaceFile()
    {
        string[] sources =
        [
            """
            using App.Contracts;

            namespace App.Users;

            internal sealed class {|#0:GetUserHandler|} : IRequestHandler<int, string> { }
            """,
            """
            using App.Contracts;

            namespace App.Orders;

            internal sealed class {|#1:GetOrderHandler|} : IRequestHandler<long, string> { }
            """,
        ];
        string[] fixedSources =
        [
            """
            using App.Contracts;

            namespace App.Users;

            internal sealed class GetUserHandler : IGetUserHandler { }
            """,
            """
            using App.Contracts;

            namespace App.Orders;

            internal sealed class GetOrderHandler : IGetOrderHandler { }
            """,
        ];
        (string, string)[] addedFiles =
        [
            ("IGetUserHandler.cs", """
                using App.Contracts;

                namespace App.Users;

                public interface IGetUserHandler : IRequestHandler<int, string>;

                """),
            ("IGetOrderHandler.cs", """
                using App.Contracts;

                namespace App.Orders;

                public interface IGetOrderHandler : IRequestHandler<long, string>;

                """),
        ];

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            sources,
            fixedSources,
            addedFiles,
            GetUserHandlerDiagnostic(),
            Diagnostic(Descriptors.CompanionInterfaceMissing)
                .WithLocation(1)
                .WithArguments(
                    "GetOrderHandler",
                    "IGetOrderHandler",
                    "'IRequestHandler'",
                    "'handlers'"));
    }

    [Fact]
    public async Task FixAll_WithNullEquivalenceKey_FixesOnlyDiagnosticsThatHaveAFix()
    {
        string[] sources =
        [
            // No safe fix: the companion exists but doesn't extend IRequestHandler.
            """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler;

            internal sealed class GetUserHandler : IGetUserHandler, IRequestHandler<int, string> { }
            """,
            """
            using App.Contracts;

            namespace App.Orders;

            internal sealed class GetOrderHandler : IRequestHandler<long, string> { }
            """,
            """
            using App.Contracts;

            namespace App.Cities;

            public interface IGetCityHandler : IRequestHandler<int, string>;

            internal sealed class GetCityHandler : IRequestHandler<int, string> { }
            """,
        ];

        // dotnet format passes a null key when the first diagnostic it sees has no fix.
        Dictionary<string, SourceText> documents =
            await FixAllAsync<CompanionInterfaceCodeFixProvider>(Handlers, sources, null);

        Assert.Equal(
            ["Contracts.cs", "IGetOrderHandler.cs", "Test0.cs", "Test1.cs", "Test2.cs"],
            documents.Keys.Order());
        Assert.Equal(sources[0], documents["Test0.cs"].ToString());
        Assert.Equal(
            """
            using App.Contracts;

            namespace App.Orders;

            internal sealed class GetOrderHandler : IGetOrderHandler { }
            """,
            documents["Test1.cs"].ToString());
        Assert.Equal(
            """
            using App.Contracts;

            namespace App.Orders;

            public interface IGetOrderHandler : IRequestHandler<long, string>;

            """,
            documents["IGetOrderHandler.cs"].ToString());
        Assert.Equal(
            """
            using App.Contracts;

            namespace App.Cities;

            public interface IGetCityHandler : IRequestHandler<int, string>;

            internal sealed class GetCityHandler : IRequestHandler<int, string>, IGetCityHandler { }
            """,
            documents["Test2.cs"].ToString());

        // The new file takes the source file's encoding: UTF-8 without a BOM here.
        Assert.Empty(documents["IGetOrderHandler.cs"].Encoding!.GetPreamble());
    }

    [Fact]
    public async Task Fix_WithMatchedInterfaceFromBaseClass_CompanionExtendsIt()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            internal abstract class HandlerBase : IRequestHandler<int, string> { }

            internal sealed class {|#0:GetUserHandler|} : HandlerBase { }
            """;
        const string fixedSource = """
            using App.Contracts;

            namespace App.Users;

            internal abstract class HandlerBase : IRequestHandler<int, string> { }

            internal sealed class GetUserHandler : HandlerBase, IGetUserHandler { }
            """;
        const string companion = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            [source],
            [fixedSource],
            [("IGetUserHandler.cs", companion)],
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Fix_WithInternalTypeArgument_GeneratesInternalInterface()
    {
        const string source = """
            namespace App.Users
            {
                internal sealed class GetUser { }

                internal sealed class {|#0:GetUserHandler|}
                    : App.Contracts.IRequestHandler<GetUser, string> { }
            }
            """;
        const string fixedSource = """
            namespace App.Users
            {
                internal sealed class GetUser { }

                internal sealed class GetUserHandler
                    : IGetUserHandler { }
            }
            """;
        const string companion = """
            using App.Contracts;

            namespace App.Users
            {
                internal interface IGetUserHandler : IRequestHandler<GetUser, string>;
            }

            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            [source],
            [fixedSource],
            [("IGetUserHandler.cs", companion)],
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Fix_WithMultilineBaseList_KeepsAuthorsLayout()
    {
        const string source = """
            using System;
            using App.Contracts;

            namespace App.Users;

            internal sealed class {|#0:GetUserHandler|} :
                IRequestHandler<int, string>,
                IDisposable
            {
                public void Dispose() { }
            }
            """;
        const string fixedSource = """
            using System;
            using App.Contracts;

            namespace App.Users;

            internal sealed class GetUserHandler :
                IGetUserHandler,
                IDisposable
            {
                public void Dispose() { }
            }
            """;
        const string companion = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            [source],
            [fixedSource],
            [("IGetUserHandler.cs", companion)],
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Fix_WithGenericType_InsertsCompanionIntoSameFile()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            internal sealed class {|#0:Lookup|}<T> : IRequestHandler<T, string>
                where T : class
            {
            }
            """;
        const string fixedSource = """
            using App.Contracts;

            namespace App.Users;

            public interface ILookup<T> : IRequestHandler<T, string>
                where T : class;

            internal sealed class Lookup<T> : ILookup<T>
                where T : class
            {
            }
            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            source,
            fixedSource,
            Diagnostic(Descriptors.CompanionInterfaceMissing)
                .WithLocation(0)
                .WithArguments("Lookup", "ILookup<T>", "'IRequestHandler'", "'handlers'"));
    }

    [Fact]
    public async Task Fix_WithExistingCompanionNotImplemented_AddsItToBaseList()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            internal sealed class {|#0:GetUserHandler|} : IRequestHandler<int, string> { }
            """;
        const string fixedSource = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler : IRequestHandler<int, string>;

            internal sealed class GetUserHandler : IRequestHandler<int, string>, IGetUserHandler { }
            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            source,
            fixedSource,
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Fix_WithCompanionNotExtendingMatchedInterface_OffersNoFix()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            public interface IGetUserHandler;

            internal sealed class {|#0:GetUserHandler|}
                : IGetUserHandler, IRequestHandler<int, string> { }
            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            source,
            source,
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Fix_WithNonInterfaceTypeNamedLikeCompanion_OffersNoFix()
    {
        const string source = """
            using App.Contracts;

            namespace App.Users;

            internal sealed class IGetUserHandler { }

            internal sealed class {|#0:GetUserHandler|} : IRequestHandler<int, string> { }
            """;

        await VerifyCodeFixAsync<CompanionInterfaceCodeFixProvider>(
            Handlers,
            source,
            source,
            GetUserHandlerDiagnostic());
    }

    [Fact]
    public async Task Parse_WithCompanionInterfaceAndClassMatch_ReportsTR000()
    {
        const string configuration = """
            typerules.entities.match = T:App.Entity`1
            typerules.entities.companion_interface = true
            """;
        const string source = """
            namespace App;

            internal sealed class Order : Entity<int> { }
            """;

        await VerifyAnalyzerAsync(
            configuration,
            source,
            Diagnostic(Descriptors.InvalidConfiguration).WithArguments(
                "rule set 'entities' sets companion_interface, so every match type must be an "
                + "interface, but 'T:App.Entity`1' is not"));
    }
}
