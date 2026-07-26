using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class SourceResolutionTests
{
    [Fact]
    public async Task Source_SourceMethod_SingleBlockWithDocAttributesSignatureBodyAndSpan()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            using System;
            namespace Demo;
            public class Svc
            {
                /// <summary>Runs.</summary>
                [Obsolete]
                public int Run(int n) { return n + 1; }
            }
            """));

        var groups = await DaemonServer.ResolveSourceAsync(solution, "Demo.Svc.Run", null, null, null);

        var result = Result(Assert.Single(groups));
        Assert.True(result.HasSource);
        var block = Assert.Single(result.Blocks);
        Assert.Contains("/// <summary>Runs.</summary>", block.Text);
        Assert.Contains("[Obsolete]", block.Text);
        Assert.Contains("public int Run(int n)", block.Text);
        Assert.Contains("return n + 1;", block.Text);
        Assert.True(block.StartLine <= block.EndLine);
        Assert.Equal("/virtual/Svc.cs", block.File);
        // The span bounds the verbatim text, which begins at the leading XML-doc (line 5), not the
        // signature (line 7).
        Assert.Equal(5, block.StartLine);
        Assert.Equal(7, block.EndLine);
    }

    [Fact]
    public async Task Source_PartialClass_TwoFiles_YieldsOneBlockPerFile()
    {
        var solution = BuildSolution(
            ("/virtual/Part1.cs", """
                namespace Demo;
                public partial class Widget
                {
                    public void A() {}
                }
                """),
            ("/virtual/Part2.cs", """
                namespace Demo;
                public partial class Widget
                {
                    public void B() {}
                }
                """));

        var groups = await DaemonServer.ResolveSourceAsync(solution, "Demo.Widget", null, null, null);

        var result = Result(Assert.Single(groups));
        Assert.True(result.HasSource);
        Assert.Equal(2, result.Blocks.Count);
        Assert.Contains(result.Blocks, b => b.File == "/virtual/Part1.cs");
        Assert.Contains(result.Blocks, b => b.File == "/virtual/Part2.cs");
    }

    [Fact]
    public async Task Source_AbstractMethod_NoBody_EndsAtSemicolonNoError()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public abstract class Svc
            {
                public abstract int Run(int n);
            }
            """));

        var groups = await DaemonServer.ResolveSourceAsync(solution, "Demo.Svc.Run", null, null, null);

        var result = Result(Assert.Single(groups));
        Assert.True(result.HasSource);
        var block = Assert.Single(result.Blocks);
        Assert.EndsWith(";", block.Text.TrimEnd());
        Assert.DoesNotContain("{", block.Text);
    }

    [Fact]
    public async Task Source_MetadataSymbol_NonErrorNoSourceWithAssembly()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public class Svc
            {
                public string Greet(string name) => name.Substring(0);
            }
            """));

        // Point at "Substring" (line 4, col 47) → metadata System.String.Substring.
        var groups = await DaemonServer.ResolveSourceAsync(
            solution, symbol: null, file: "/virtual/Svc.cs", line: 4, col: 47);

        var result = Result(Assert.Single(groups));
        Assert.False(result.HasSource);
        Assert.Empty(result.Blocks);
        Assert.NotNull(result.Assembly);
        Assert.Contains("no source", result.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Source_ImplicitConstructor_NonErrorGeneratedNote()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public class Svc {}
            """));

        var groups = await DaemonServer.ResolveSourceAsync(solution, "Demo.Svc.Svc", null, null, null);

        var result = Result(Assert.Single(groups));
        Assert.False(result.HasSource);
        Assert.Contains("generated", result.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Source_OverloadedSymbol_OneGroupPerOverload()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public class Svc
            {
                public void Run(string s) {}
                public void Run(int n) {}
            }
            """));

        var groups = await DaemonServer.ResolveSourceAsync(solution, "Demo.Svc.Run", null, null, null);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.True(Result(g).HasSource));
    }

    [Fact]
    public async Task Source_Validation_RejectsNeitherMode()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", "namespace Demo; public class Svc {}"));

        await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveSourceAsync(solution, null, null, null, null));
    }

    [Fact]
    public async Task Source_Validation_RejectsBothModes()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", "namespace Demo; public class Svc {}"));

        await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveSourceAsync(solution, "Demo.Svc", "/virtual/Svc.cs", 1, 1));
    }

    private static SourceResult Result(SymbolMatchGroup<SourceResult> group) => group.Result;

    private static Solution BuildSolution(params (string Path, string Code)[] files)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(string).Assembly.Location));

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
