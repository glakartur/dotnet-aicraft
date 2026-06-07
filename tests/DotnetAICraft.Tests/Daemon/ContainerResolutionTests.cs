using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class ContainerResolutionTests
{
    [Fact]
    public async Task ContainersInFileAsync_MultipleTopLevelTypes_ReturnedInSourceOrder()
    {
        var solution = BuildSolution(("/virtual/Types.cs", """
            namespace Demo.Block
            {
                public class First {}
                public enum Second { A, B }
            }

            namespace Demo.Scoped;
            public class Third
            {
                public class Nested {} // nested — must NOT be a top-level container
            }
            public delegate int Fourth(string s);
            """));

        var containers = await SymbolResolver.ContainersInFileAsync(solution, "/virtual/Types.cs");

        Assert.Equal(
            new[] { "First", "Second", "Third", "Fourth" },
            containers.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task ContainersInFileAsync_EmptyFile_ReturnsEmptyListNotError()
    {
        var solution = BuildSolution(("/virtual/Empty.cs", "using System;\n"));

        var containers = await SymbolResolver.ContainersInFileAsync(solution, "/virtual/Empty.cs");

        Assert.Empty(containers);
    }

    [Fact]
    public async Task ContainersInFileAsync_TopLevelStatements_ResolvesSynthesizedEntryType()
    {
        var solution = BuildConsoleSolution(("/virtual/Program.cs", """
            System.Console.WriteLine("hello");
            """));

        var containers = await SymbolResolver.ContainersInFileAsync(solution, "/virtual/Program.cs");

        Assert.Single(containers);
        Assert.Contains(containers, c => c.GetMembers().Any(m => m.Name.Contains("Main")));
    }

    [Fact]
    public async Task ResolveContainerTargetAsync_Type_ReturnsTypes()
    {
        var solution = BuildSolution(("/virtual/T.cs", """
            namespace Demo;
            public class Widget {}
            """));

        var target = await SymbolResolver.ResolveContainerTargetAsync(solution, "Demo.Widget");

        Assert.Equal(SymbolResolver.ContainerTargetKind.Types, target.Kind);
        Assert.Single(target.Types);
        Assert.Equal("Widget", target.Types[0].Name);
    }

    [Fact]
    public async Task ResolveContainerTargetAsync_Method_ReturnsMemberSignal()
    {
        var solution = BuildSolution(("/virtual/T.cs", """
            namespace Demo;
            public class Widget { public void Render() {} }
            """));

        var target = await SymbolResolver.ResolveContainerTargetAsync(solution, "Demo.Widget.Render");

        Assert.Equal(SymbolResolver.ContainerTargetKind.Member, target.Kind);
        Assert.NotEmpty(target.Members);
    }

    [Fact]
    public async Task ResolveContainerTargetAsync_Namespace_ReturnsNamespaceSignal()
    {
        var solution = BuildSolution(("/virtual/T.cs", """
            namespace Demo.Services;
            public class Widget {}
            """));

        var target = await SymbolResolver.ResolveContainerTargetAsync(solution, "Demo.Services");

        Assert.Equal(SymbolResolver.ContainerTargetKind.Namespace, target.Kind);
    }

    [Fact]
    public async Task ResolveContainerTargetAsync_TypePreferredOverSameLeafNamespace()
    {
        var solution = BuildSolution(("/virtual/T.cs", """
            namespace Demo;
            public class Widget {}

            namespace Other.Widget;
            public class Unrelated {}
            """));

        // A namespace named "Widget" exists elsewhere, but the FQN resolves to the type.
        var target = await SymbolResolver.ResolveContainerTargetAsync(solution, "Demo.Widget");

        Assert.Equal(SymbolResolver.ContainerTargetKind.Types, target.Kind);
    }

    [Fact]
    public async Task ResolveContainerTargetAsync_Unknown_Throws()
    {
        var solution = BuildSolution(("/virtual/T.cs", "namespace Demo; public class Widget {}"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => SymbolResolver.ResolveContainerTargetAsync(solution, "Demo.DoesNotExist"));
    }

    private static Solution BuildSolution(params (string Path, string Code)[] files)
        => BuildSolution(OutputKind.DynamicallyLinkedLibrary, files);

    private static Solution BuildConsoleSolution(params (string Path, string Code)[] files)
        => BuildSolution(OutputKind.ConsoleApplication, files);

    private static Solution BuildSolution(OutputKind outputKind, (string Path, string Code)[] files)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        solution = solution.WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(outputKind));
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        foreach (var (path, code) in files)
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(path),
                Microsoft.CodeAnalysis.Text.SourceText.From(code),
                filePath: path);
        }

        return solution;
    }
}
