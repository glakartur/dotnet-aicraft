using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotnetAICraft.Tests.Roslyn;

public class SymbolResolverFromFullNameTests
{
    [Fact]
    public async Task FromFullNameAsync_Method_ResolvedByContainingTypePath()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public static System.Threading.Tasks.Task RunAsync(string arg)
                    => System.Threading.Tasks.Task.CompletedTask;
                public void OtherMethod() {}
            }
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(solution, "Demo.Services.MyService.RunAsync");

        Assert.NotNull(symbol);
        Assert.Equal("RunAsync", symbol.Name);
        Assert.Equal(SymbolKind.Method, symbol.Kind);
    }

    [Fact]
    public async Task FromFullNameAsync_Type_StillResolves()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;
            public class MyService {}
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(solution, "Demo.Services.MyService");

        Assert.NotNull(symbol);
        Assert.Equal("MyService", symbol.Name);
        Assert.Equal(SymbolKind.NamedType, symbol.Kind);
    }

    [Fact]
    public async Task FromFullNameAsync_Constructor_ResolvedByRepeatedTypeName()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public MyService(string arg) {}
            }
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(solution, "Demo.Services.MyService.MyService");

        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Method, symbol.Kind);
        Assert.Equal(MethodKind.Constructor, ((IMethodSymbol)symbol).MethodKind);
    }

    [Fact]
    public async Task FromFullNameAsync_Constructor_ResolvedByDisplayStringWithParameters()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public MyService(string arg) {}
            }
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(
            solution, "Demo.Services.MyService.MyService(string)");

        Assert.Equal(MethodKind.Constructor, ((IMethodSymbol)symbol).MethodKind);
    }

    [Fact]
    public async Task FromFullNameAsync_ImplicitDefaultConstructor_Resolves()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;
            public class MyService {}
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(solution, "Demo.Services.MyService.MyService");

        Assert.Equal(MethodKind.Constructor, ((IMethodSymbol)symbol).MethodKind);
        Assert.True(symbol.IsImplicitlyDeclared);
    }

    [Fact]
    public async Task FromFullNameAsync_MethodWithParameterSignature_RoundTripsSymbolsOutput()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public void Run(string arg) {}
            }
            """);

        var symbol = await SymbolResolver.FromFullNameAsync(
            solution, "Demo.Services.MyService.Run(string)");

        Assert.Equal("Run", symbol.Name);
        Assert.Equal(SymbolKind.Method, symbol.Kind);
    }

    [Fact]
    public async Task FromFullNameAllAsync_OverloadedMethod_ReturnsAllMatches()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public void Run(string arg) {}
                public void Run(int arg) {}
            }
            """);

        var symbols = await SymbolResolver.FromFullNameAllAsync(solution, "Demo.Services.MyService.Run");

        Assert.Equal(2, symbols.Count);
        Assert.All(symbols, s => Assert.Equal("Run", s.Name));
    }

    [Fact]
    public async Task FromFullNameAsync_AmbiguousOverload_ThrowsWithCandidateList()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public class MyService
            {
                public void Run(string arg) {}
                public void Run(int arg) {}
            }
            """);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SymbolResolver.FromFullNameAsync(solution, "Demo.Services.MyService.Run"));

        Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run(string)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Run(int)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FromFullNameAllAsync_ExplicitConstructorOnly_DoesNotInventParameterlessCtor()
    {
        var solution = BuildSolution("""
            namespace Demo.Services;

            public sealed class MyService
            {
                private MyService(int x) {}
            }
            """);

        var matches = await SymbolResolver.FromFullNameAllAsync(solution, "Demo.Services.MyService.MyService");

        // The class declares an explicit ctor, so C# does NOT synthesize a parameterless one.
        var match = Assert.Single(matches);
        Assert.Equal("Demo.Services.MyService.MyService(int)", match.ToDisplayString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FromLocationAsync_ColumnBelowOne_ThrowsArgumentExceptionNotRawRangeError(int col)
    {
        var solution = BuildSolution("namespace Demo; public class Foo {}");

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SymbolResolver.FromLocationAsync(solution, "/virtual/Test.cs", line: 1, col: col));

        // ArgumentOutOfRangeException derives from ArgumentException, so the daemon maps it to
        // INVALID_PARAMS rather than INTERNAL_ERROR.
        Assert.IsAssignableFrom<ArgumentException>(ex);
        Assert.Equal("col", ex.ParamName);
    }

    [Fact]
    public async Task FromFullNameAsync_UnknownSymbol_ThrowsArgumentException()
    {
        var solution = BuildSolution("namespace Demo; public class Foo {}");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SymbolResolver.FromFullNameAsync(solution, "Demo.DoesNotExist"));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Solution BuildSolution(string code)
    {
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId), "Test.cs",
            Microsoft.CodeAnalysis.Text.SourceText.From(code),
            filePath: "/virtual/Test.cs");
        return solution;
    }
}
