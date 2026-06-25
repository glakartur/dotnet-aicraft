using System.Text.Json;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Output;

public class HierarchyNodeSerializationTests
{
    private static HierarchyNode Leaf(string name) => new(
        Name: name,
        FullName: $"MyApp.Animals.{name}",
        Kind: "class",
        File: $"src/Animals/{name}.cs",
        Line: 3,
        Col: 18,
        ContainingType: null,
        ContainingNamespace: "MyApp.Animals",
        Truncated: false,
        Children: []);

    [Fact]
    public void TwoLevelTree_SerializesCamelCaseNestedChildren_LeafHasEmptyArray()
    {
        var root = new HierarchyNode(
            Name: "Animal",
            FullName: "MyApp.Animals.Animal",
            Kind: "class",
            File: "src/Animals/Animal.cs",
            Line: 5,
            Col: 18,
            ContainingType: null,
            ContainingNamespace: "MyApp.Animals",
            Truncated: false,
            Children: [Leaf("Dog")]);

        var json = JsonOutput.Serialize(root);

        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        Assert.Equal("Animal", rootEl.GetProperty("name").GetString());
        Assert.Equal("MyApp.Animals.Animal", rootEl.GetProperty("fullName").GetString());
        Assert.False(rootEl.GetProperty("truncated").GetBoolean());

        var children = rootEl.GetProperty("children");
        Assert.Equal(JsonValueKind.Array, children.ValueKind);
        var dog = Assert.Single(children.EnumerateArray());
        Assert.Equal("Dog", dog.GetProperty("name").GetString());
        Assert.Equal(0, dog.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public void TruncatedNode_SerializesTruncatedTrue_AndOmitsNullContainingType()
    {
        var node = new HierarchyNode(
            Name: "Puppy",
            FullName: "MyApp.Animals.Puppy",
            Kind: "class",
            File: "src/Animals/Puppy.cs",
            Line: 4,
            Col: 18,
            ContainingType: null,
            ContainingNamespace: "MyApp.Animals",
            Truncated: true,
            Children: []);

        var json = JsonOutput.Serialize(node);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("containingType", out _));
    }

    [Fact]
    public void RoundTrips_PreservingNestingAndTruncatedFlag()
    {
        var truncatedChild = new HierarchyNode(
            Name: "Puppy",
            FullName: "MyApp.Animals.Puppy",
            Kind: "class",
            File: "src/Animals/Puppy.cs",
            Line: 4,
            Col: 18,
            ContainingType: null,
            ContainingNamespace: "MyApp.Animals",
            Truncated: true,
            Children: []);

        var root = new HierarchyNode(
            Name: "Animal",
            FullName: "MyApp.Animals.Animal",
            Kind: "class",
            File: "src/Animals/Animal.cs",
            Line: 5,
            Col: 18,
            ContainingType: null,
            ContainingNamespace: "MyApp.Animals",
            Truncated: false,
            Children: [truncatedChild]);

        var json = JsonOutput.Serialize(root);
        var back = JsonOutput.Deserialize<HierarchyNode>(json);

        Assert.NotNull(back);
        Assert.Equal("MyApp.Animals.Animal", back!.FullName);
        var child = Assert.Single(back.Children);
        Assert.Equal("MyApp.Animals.Puppy", child.FullName);
        Assert.True(child.Truncated);
    }
}
