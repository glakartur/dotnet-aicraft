using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

public class SymbolMatchGroupingTests
{
    private const string Source = """
        namespace Demo;

        public class Svc
        {
            public void Run(string s) {}
            public void Run(int i) {}

            public void Caller()
            {
                Run("a");
                Run(1);
            }
        }
        """;

    [Fact]
    public async Task Refs_OverloadedNameWithoutParameters_ReturnsOneGroupPerOverload()
    {
        using var fixture = CreateSolution();

        var groups = await DotnetAICraft.Commands.Refs.UseCase.ResolveAsync(
            fixture.Solution, "Demo.Svc.Run", null, null, null);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Symbol == "Demo.Svc.Run(string)");
        Assert.Contains(groups, g => g.Symbol == "Demo.Svc.Run(int)");
        // Each overload is called exactly once, in its own group — not merged.
        Assert.All(groups, g => Assert.Single(g.Result));
    }

    [Fact]
    public async Task Refs_SingleMatch_ReturnsOneGroup()
    {
        using var fixture = CreateSolution();

        var groups = await DotnetAICraft.Commands.Refs.UseCase.ResolveAsync(
            fixture.Solution, "Demo.Svc.Caller", null, null, null);

        var group = Assert.Single(groups);
        Assert.Equal("Demo.Svc.Caller()", group.Symbol);
        Assert.Equal("method", group.Kind);
    }

    [Fact]
    public async Task Callers_OverloadedNameWithoutParameters_ReturnsOneGraphPerOverload()
    {
        using var fixture = CreateSolution();

        var result = await DotnetAICraft.Commands.Callers.UseCase.ResolveAsync(
            fixture.Solution, "Demo.Svc.Run", null, null, null, "incoming", 1);

        var groups = result;
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.NotNull(g.Result));
    }

    private static SolutionFixture CreateSolution()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Concat(new[]
            {
                typeof(CSharpCompilation).Assembly,
                typeof(CSharpFormattingOptions).Assembly
            })
            .Distinct();

        var workspace = new AdhocWorkspace(MefHostServices.Create(assemblies));
        var projectId = ProjectId.CreateNewId(debugName: "GroupingProject");

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "GroupingProject", "GroupingProject", LanguageNames.CSharp)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(projectId), "Svc.cs", SourceText.From(Source),
                filePath: "/virtual/src/Svc.cs");

        return new SolutionFixture(workspace, solution);
    }

    private sealed class SolutionFixture(AdhocWorkspace workspace, Solution solution) : IDisposable
    {
        public Solution Solution { get; } = solution;
        public void Dispose() => workspace.Dispose();
    }
}
