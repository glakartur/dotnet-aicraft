using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputSourceTests
{
    [Fact]
    public void SingleBlock_RendersSpanHeaderAndVerbatimText()
    {
        var result = new SourceResult(
            "Demo.Svc.Run", "method", HasSource: true,
            new[] { new SourceBlock("src/Svc.cs", 5, 7, "    public int Run(int n)\n    {\n        return n;\n    }") },
            Assembly: null, Note: null);

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSource(result, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("source:", output);
        Assert.Contains("src/Svc.cs:5-7:", output);
        Assert.Contains("public int Run(int n)", output);
    }

    [Fact]
    public void MultipleBlocks_RendersPartsAnnotationAndEachSpan()
    {
        var result = new SourceResult(
            "Demo.Widget", "class", HasSource: true,
            new[]
            {
                new SourceBlock("src/Part1.cs", 2, 5, "public partial class Widget { }"),
                new SourceBlock("src/Part2.cs", 2, 5, "public partial class Widget { }")
            },
            Assembly: null, Note: null);

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSource(result, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("source: (2 parts)", output);
        Assert.Contains("src/Part1.cs:2-5:", output);
        Assert.Contains("src/Part2.cs:2-5:", output);
    }

    [Fact]
    public void MetadataDegraded_RendersNoteWithoutBlocks()
    {
        var result = new SourceResult(
            "System.String.Substring", "method", HasSource: false,
            Array.Empty<SourceBlock>(),
            Assembly: "System.Private.CoreLib",
            Note: "no source available — declared in metadata (System.Private.CoreLib)");

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSource(result, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("source: (no source available — declared in metadata (System.Private.CoreLib))", output);
    }
}
