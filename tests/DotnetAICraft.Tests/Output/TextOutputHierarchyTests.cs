using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputHierarchyTests
{
    private static HierarchyNode Node(
        string name,
        string fullName,
        string kind = "class",
        string file = "src/X.cs",
        int line = 1,
        int col = 1,
        bool truncated = false,
        IReadOnlyList<HierarchyNode>? children = null)
        => new(name, fullName, kind, file, line, col, null, "Demo", truncated, children ?? []);

    private static string[] Render(HierarchyNode root, string direction)
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteHierarchy(root, direction, "S.sln");
        return cap.GetOutput().Split(Environment.NewLine);
    }

    [Fact]
    public void TwoLevelChain_RendersIndentByDepth()
    {
        var root = Node("Animal", "Demo.Animal", file: "src/Animal.cs", line: 5, col: 18,
            children: [
                Node("Dog", "Demo.Dog", file: "src/Dog.cs", line: 3, col: 18,
                    children: [Node("Puppy", "Demo.Puppy", file: "src/Puppy.cs", line: 4, col: 18)])
            ]);

        var lines = Render(root, "down");

        Assert.Equal("hierarchy (down):", lines[0]);
        Assert.Equal("src/Animal.cs:5:18: class Demo.Animal", lines[1]);
        Assert.Equal("  src/Dog.cs:3:18: class Demo.Dog", lines[2]);
        Assert.Equal("    src/Puppy.cs:4:18: class Demo.Puppy", lines[3]);
    }

    [Fact]
    public void TruncatedNode_EndsWithSuffix_AndRendersNoChildren()
    {
        var root = Node("Animal", "Demo.Animal", file: "src/Animal.cs", line: 5, col: 18,
            children: [Node("Dog", "Demo.Dog", file: "src/Dog.cs", line: 3, col: 18, truncated: true)]);

        var lines = Render(root, "down");

        Assert.Equal("  src/Dog.cs:3:18: class Demo.Dog (truncated)", lines[2]);
        Assert.DoesNotContain(lines, l => l.Contains("Puppy"));
    }

    [Fact]
    public void MetadataNode_RendersKindAndFullNameWithoutLocationPrefix()
    {
        var root = Node("Animal", "Demo.Animal", file: "src/Animal.cs", line: 5, col: 18,
            children: [Node("Object", "object", file: "", line: 0, col: 0)]);

        var lines = Render(root, "up");

        Assert.Equal("hierarchy (up):", lines[0]);
        Assert.Equal("src/Animal.cs:5:18: class Demo.Animal", lines[1]);
        Assert.Equal("  class object", lines[2]);
    }

    [Fact]
    public void RootWithNoChildren_Down_RendersNoDerivedTypesAnnotation()
    {
        var root = Node("Coord", "Demo.Coord", kind: "struct");

        var lines = Render(root, "down");

        Assert.Equal("hierarchy (down): (no derived types)", lines[0]);
    }

    [Fact]
    public void RootWithNoChildren_Up_RendersNoBaseTypesAnnotation()
    {
        var root = Node("Animal", "Demo.Animal");

        var lines = Render(root, "up");

        Assert.Equal("hierarchy (up): (no base types)", lines[0]);
    }

    [Fact]
    public void Diamond_SharedNodeAppearsTwice_EachUnderItsParent()
    {
        var iBaseLeft = Node("IBase", "Demo.IBase", kind: "interface", file: "src/IBase.cs", line: 1, col: 18);
        var iBaseRight = Node("IBase", "Demo.IBase", kind: "interface", file: "src/IBase.cs", line: 1, col: 18);
        var root = Node("IDiamond", "Demo.IDiamond", kind: "interface", file: "src/ID.cs", line: 1, col: 18,
            children: [
                Node("ILeft", "Demo.ILeft", kind: "interface", file: "src/IL.cs", line: 1, col: 18, children: [iBaseLeft]),
                Node("IRight", "Demo.IRight", kind: "interface", file: "src/IR.cs", line: 1, col: 18, children: [iBaseRight])
            ]);

        var lines = Render(root, "up");

        // Shared base emitted once per path at depth 2 (4-space indent), no marker (D8).
        Assert.Equal(2, lines.Count(l => l == "    src/IBase.cs:1:18: interface Demo.IBase"));
    }

    [Fact]
    public void GenericBase_RendersConstructedDisplayVerbatim()
    {
        var root = Node("StringBox", "Demo.StringBox", file: "src/SB.cs", line: 1, col: 18,
            children: [Node("Box", "Demo.Box<string>", file: "src/Box.cs", line: 1, col: 18)]);

        var lines = Render(root, "up");

        Assert.Equal("  src/Box.cs:1:18: class Demo.Box<string>", lines[2]);
    }
}
