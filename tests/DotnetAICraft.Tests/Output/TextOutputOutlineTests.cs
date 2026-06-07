using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputOutlineTests
{
    [Fact]
    public void Declared_RendersFlatLocatedLines_NestedCarriesDeclaringType()
    {
        var result = new OutlineResult(
            "Demo.Outer", "class", PublicOnly: false, IncludeInherited: false,
            new[]
            {
                new OutlineMember("src/Outer.cs", 4, 12, "Demo.Outer", "public string Render()", null),
                new OutlineMember("src/Outer.cs", 8, 21, "Demo.Outer.Inner", "public void N()", null)
            },
            Array.Empty<OutlineInheritedGroup>());

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteOutline(result, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("outline:", output);
        Assert.Contains("src/Outer.cs:4:12: public string Render()\n", output);
        // Nested member carries its declaring type; the container's own members do not.
        Assert.Contains("src/Outer.cs:8:21: public void N()  [Demo.Outer.Inner]", output);
        Assert.DoesNotContain("public string Render()  [", output);
    }

    [Fact]
    public void IncludeInherited_RendersGroupedHeaderWithAssemblyAndTags()
    {
        var result = new OutlineResult(
            "Demo.Widget", "class", PublicOnly: false, IncludeInherited: true,
            new[] { new OutlineMember("src/Widget.cs", 3, 17, "Demo.Widget", "public void Render()", null) },
            new[]
            {
                new OutlineInheritedGroup("Demo.Base", null, new[]
                {
                    new OutlineInheritedMember("public void Bar()", "hidden by new")
                }),
                new OutlineInheritedGroup("object", "System.Private.CoreLib", new[]
                {
                    new OutlineInheritedMember("public virtual string ToString()", null)
                })
            });

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteOutline(result, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("outline: (includeInherited)", output);
        Assert.Contains("inherited from Demo.Base:\n", output);
        Assert.Contains("  public void Bar()  (hidden by new)", output);
        Assert.Contains("inherited from object [System.Private.CoreLib]:\n", output);
        Assert.Contains("  public virtual string ToString()", output);
    }

    [Fact]
    public void Empty_RendersNoResults()
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteOutlineEmpty();
        var output = cap.GetOutput().Replace("\r\n", "\n");
        Assert.Contains("outline: (no results)", output);
    }
}
