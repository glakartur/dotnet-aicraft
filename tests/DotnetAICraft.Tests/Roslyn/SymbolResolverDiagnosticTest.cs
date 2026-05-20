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
