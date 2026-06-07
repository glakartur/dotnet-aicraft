using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class JsonFormatParityTests
{
    [Fact]
    public void Write_Refs_PrettyPrintedJson_ByteExact()
    {
        var items = new[]
        {
            new ReferenceResult("/a/F.cs", 10, 4, "ctx")
        };
        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.Write(items);
        var expected =
            "[\n" +
            "  {\n" +
            "    \"file\": \"/a/F.cs\",\n" +
            "    \"line\": 10,\n" +
            "    \"col\": 4,\n" +
            "    \"context\": \"ctx\"\n" +
            "  }\n" +
            "]\n";
        var actual = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteError_NoDetails_ByteExact()
    {
        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.WriteError("ERR", "msg");
        var expected =
            "{\n" +
            "  \"error\": {\n" +
            "    \"code\": \"ERR\",\n" +
            "    \"message\": \"msg\"\n" +
            "  }\n" +
            "}\n";
        var actual = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteError_WithHint_ByteExact()
    {
        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.WriteError("ERR", "msg", new { hint = "do this" });
        var expected =
            "{\n" +
            "  \"error\": {\n" +
            "    \"code\": \"ERR\",\n" +
            "    \"message\": \"msg\",\n" +
            "    \"details\": {\n" +
            "      \"hint\": \"do this\"\n" +
            "    }\n" +
            "  }\n" +
            "}\n";
        var actual = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteWithSolutionRoot_ArrayResult_WrapsAsItems()
    {
        var items = new[] { new ReferenceResult("Foo/Bar.cs", 10, 4, "ctx") };
        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.WriteWithSolutionRoot("/repo", items);
        var actual = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Contains("\"solutionRoot\": \"/repo\"", actual);
        Assert.Contains("\"items\":", actual);
        Assert.Contains("\"file\": \"Foo/Bar.cs\"", actual);
        var solutionRootIndex = actual.IndexOf("\"solutionRoot\"", StringComparison.Ordinal);
        var itemsIndex = actual.IndexOf("\"items\"", StringComparison.Ordinal);
        Assert.True(solutionRootIndex < itemsIndex);
    }

    [Fact]
    public void WriteWithSolutionRoot_ObjectResult_PrependsSolutionRoot()
    {
        var page = new SymbolsResultPage(
            new[] { new SymbolResult("X", "Demo.X", "class", "X.cs", 1, 1, null, "Demo") },
            HasMore: true);
        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.WriteWithSolutionRoot("/repo", page);
        var actual = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Contains("\"solutionRoot\": \"/repo\"", actual);
        Assert.Contains("\"items\":", actual);
        Assert.Contains("\"hasMore\": true", actual);
        var solutionRootIndex = actual.IndexOf("\"solutionRoot\"", StringComparison.Ordinal);
        var itemsIndex = actual.IndexOf("\"items\"", StringComparison.Ordinal);
        Assert.True(solutionRootIndex < itemsIndex);
    }

    [Fact]
    public void Write_DescribeCard_CamelCaseAndOmitsNulls()
    {
        var card = new DescribeCard(
            FullName: "Demo.Svc.Run",
            Kind: "method",
            File: "src/Svc.cs",
            Line: 5,
            Col: 17,
            ContainingType: "Demo.Svc",
            ContainingNamespace: "Demo",
            Signature: "public int Run(int n)",
            ReturnType: "int",
            Parameters: new[] { new DescribeParameter("n", "int", null) },
            Modifiers: null,
            Attributes: null,
            ConstantValue: null,
            Documentation: null,
            Siblings: null,
            Assembly: null);

        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.Write(card);
        var actual = cap.GetOutput().Replace("\r\n", "\n");

        // camelCase property names.
        Assert.Contains("\"fullName\": \"Demo.Svc.Run\"", actual);
        Assert.Contains("\"signature\": \"public int Run(int n)\"", actual);
        Assert.Contains("\"returnType\": \"int\"", actual);
        Assert.Contains("\"parameters\":", actual);

        // null members are omitted (DefaultIgnoreCondition.WhenWritingNull).
        Assert.DoesNotContain("\"modifiers\"", actual);
        Assert.DoesNotContain("\"attributes\"", actual);
        Assert.DoesNotContain("\"constantValue\"", actual);
        Assert.DoesNotContain("\"documentation\"", actual);
        Assert.DoesNotContain("\"siblings\"", actual);
        Assert.DoesNotContain("\"assembly\"", actual);
        Assert.DoesNotContain("\"defaultValue\"", actual);
    }

    [Fact]
    public void Write_SourceResult_MultiBlock_CamelCase()
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
        JsonOutput.Write(result);
        var actual = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("\"hasSource\": true", actual);
        Assert.Contains("\"blocks\":", actual);
        Assert.Contains("\"startLine\": 2", actual);
        Assert.Contains("\"endLine\": 5", actual);
        Assert.DoesNotContain("\"assembly\"", actual); // null omitted
        Assert.DoesNotContain("\"note\"", actual);
    }

    [Fact]
    public void Write_SourceResult_MetadataDegraded_OmitsBlocksContentButKeepsNote()
    {
        var result = new SourceResult(
            "System.String.Substring", "method", HasSource: false,
            Array.Empty<SourceBlock>(),
            Assembly: "System.Private.CoreLib",
            Note: "no source available");

        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.Write(result);
        var actual = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("\"hasSource\": false", actual);
        Assert.Contains("\"blocks\": []", actual); // non-nullable list always serialized, even when empty
        Assert.Contains("\"assembly\": \"System.Private.CoreLib\"", actual);
        Assert.Contains("\"note\": \"no source available\"", actual);
    }

    [Fact]
    public void Write_OutlineResult_DeclaredAndInheritedGroups_CamelCase()
    {
        var result = new OutlineResult(
            "Demo.Widget", "class", PublicOnly: true, IncludeInherited: true,
            new[] { new OutlineMember("src/Widget.cs", 3, 17, "Demo.Widget", "public void Render()", null) },
            new[]
            {
                new OutlineInheritedGroup("object", "System.Private.CoreLib", new[]
                {
                    new OutlineInheritedMember("public virtual string ToString()", null)
                })
            });

        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.Write(result);
        var actual = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("\"publicOnly\": true", actual);
        Assert.Contains("\"includeInherited\": true", actual);
        Assert.Contains("\"declared\":", actual);
        Assert.Contains("\"declaringType\": \"Demo.Widget\"", actual);
        Assert.Contains("\"inherited\":", actual);
        Assert.Contains("\"assembly\": \"System.Private.CoreLib\"", actual);
        // null Tag omitted.
        Assert.DoesNotContain("\"tag\"", actual);
    }

    [Fact]
    public void Write_OutlineResult_NoInherited_StillEmitsEmptyInheritedArray()
    {
        var result = new OutlineResult(
            "Demo.Widget", "class", PublicOnly: false, IncludeInherited: false,
            new[] { new OutlineMember("src/Widget.cs", 3, 17, "Demo.Widget", "public void Render()", null) },
            Array.Empty<OutlineInheritedGroup>());

        using var cap = ConsoleOutputCapture.Start();
        JsonOutput.Write(result);
        var actual = cap.GetOutput().Replace("\r\n", "\n");

        // Non-nullable list: the key is always present as [] so consumers can rely on it.
        Assert.Contains("\"inherited\": []", actual);
    }

    [Fact]
    public void WriteSolutionRootHeader_TextFormat_WritesHeaderAndBlankLine()
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSolutionRootHeader("/repo");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("SolutionRoot: /repo", lines[0]);
        Assert.Equal("", lines[1]);
    }
}
