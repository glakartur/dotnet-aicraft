using DotnetAICraft.Commands.Hierarchy;
using DotnetAICraft.Daemon;
using HierarchyValidation = DotnetAICraft.Commands.Hierarchy.CliValidation;
using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class HierarchyTraversalTests
{
    // ── down: classes ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Down_Class_Transitive_BuildsDerivedChain()
    {
        // Arrange
        using var fixture = CreateFixture();
        var animal = await ResolveTypeAsync(fixture.Solution, "Demo.Animal");

        // Act
        var root = await BuildAsync(fixture.Solution, animal, "down");

        // Assert
        Assert.Equal("Demo.Animal", root.FullName);
        var dog = Single(root, "Dog");
        var puppy = Single(dog, "Puppy");
        Assert.Empty(puppy.Children);
    }

    [Fact]
    public async Task Down_Class_CrossProject_IncludesDerivedFromOtherProject()
    {
        // Arrange
        using var fixture = CreateCrossProjectFixture();
        var baseType = await ResolveTypeAsync(fixture.Solution, "Demo.BaseType");

        // Act
        var root = await BuildAsync(fixture.Solution, baseType, "down");

        // Assert
        Assert.Contains(root.Children, c => c.FullName == "Demo.DerivedType");
    }

    // ── up: classes ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Up_Class_BaseChain_StopsBeforeObjectByDefault()
    {
        // Arrange
        using var fixture = CreateFixture();
        var puppy = await ResolveTypeAsync(fixture.Solution, "Demo.Puppy");

        // Act
        var root = await BuildAsync(fixture.Solution, puppy, "up");

        // Assert
        var dog = Single(root, "Dog");
        var animal = Single(dog, "Animal");
        Assert.Empty(animal.Children); // object omitted by default (R10)
    }

    [Fact]
    public async Task Up_Class_IncludeFramework_WalksMetadataBasesToObject()
    {
        // Arrange
        using var fixture = CreateFixture();
        var animal = await ResolveTypeAsync(fixture.Solution, "Demo.Animal");

        // Act
        var root = await BuildAsync(fixture.Solution, animal, "up", includeFramework: true);

        // Assert
        var obj = Single(root, "Object");
        Assert.Equal("", obj.File); // metadata node: location-less (R11)
        Assert.Equal(0, obj.Line);
        Assert.Empty(obj.Children);
    }

    [Fact]
    public async Task Up_Class_FrameworkBaseOmittedByDefault()
    {
        // Arrange
        using var fixture = CreateFixture();
        var myError = await ResolveTypeAsync(fixture.Solution, "Demo.MyError");

        // Act
        var root = await BuildAsync(fixture.Solution, myError, "up");

        // Assert
        Assert.Empty(root.Children); // System.Exception (and above) omitted (R10)
    }

    // ── interfaces ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Down_Interface_ReturnsDerivedInterfacesOnly_ExcludesClasses()
    {
        // Arrange
        using var fixture = CreateFixture();
        var ia = await ResolveTypeAsync(fixture.Solution, "Demo.IA");

        // Act
        var root = await BuildAsync(fixture.Solution, ia, "down");

        // Assert
        Assert.Contains(root.Children, c => c.Name == "IB");
        Assert.DoesNotContain(root.Children, c => c.Name == "CImpl"); // R8
    }

    [Fact]
    public async Task Up_Interface_ReturnsExtendedInterfaces()
    {
        // Arrange
        using var fixture = CreateFixture();
        var idiamond = await ResolveTypeAsync(fixture.Solution, "Demo.IDiamond");

        // Act
        var root = await BuildAsync(fixture.Solution, idiamond, "up");

        // Assert
        Assert.Contains(root.Children, c => c.Name == "ILeft"); // R9
        Assert.Contains(root.Children, c => c.Name == "IRight");
    }

    [Fact]
    public async Task Up_Interface_Diamond_EmitsSharedBaseOncePerPath_NoMarker()
    {
        // Arrange
        using var fixture = CreateFixture();
        var idiamond = await ResolveTypeAsync(fixture.Solution, "Demo.IDiamond");

        // Act
        var root = await BuildAsync(fixture.Solution, idiamond, "up");

        // Assert
        var left = Single(root, "ILeft");
        var right = Single(root, "IRight");
        Assert.Equal("Demo.IBase", Single(left, "IBase").FullName); // D8: appears under each path
        Assert.Equal("Demo.IBase", Single(right, "IBase").FullName);
    }

    // ── generics ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Down_OpenGenericBase_FindsConstructedDerivation()
    {
        // Arrange
        using var fixture = CreateFixture();
        var box = await ResolveTypeAsync(fixture.Solution, "Demo.Box`1"); // open Box<T>

        // Act
        var root = await BuildAsync(fixture.Solution, box, "down");

        // Assert
        Assert.Contains(root.Children, c => c.Name == "StringBox"); // R12
    }

    [Fact]
    public async Task Up_FromConstructedDerivation_ShowsConstructedBaseDisplay()
    {
        // Arrange
        using var fixture = CreateFixture();
        var stringBox = await ResolveTypeAsync(fixture.Solution, "Demo.StringBox");

        // Act
        var root = await BuildAsync(fixture.Solution, stringBox, "up");

        // Assert
        var box = Assert.Single(root.Children);
        Assert.Equal("Demo.Box<string>", box.FullName); // R13: not Box<T>/Box
    }

    // ── struct / record ──────────────────────────────────────────────────────
    [Fact]
    public async Task Down_Struct_HasNoDerivations()
    {
        // Arrange
        using var fixture = CreateFixture();
        var coord = await ResolveTypeAsync(fixture.Solution, "Demo.Coord");

        // Act
        var root = await BuildAsync(fixture.Solution, coord, "down");

        // Assert
        Assert.Equal("struct", root.Kind);
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task Record_ResolvesAsClass_UpAndDownBehaveAsClass()
    {
        // Arrange
        using var fixture = CreateFixture();
        var employee = await ResolveTypeAsync(fixture.Solution, "Demo.Employee");
        var person = await ResolveTypeAsync(fixture.Solution, "Demo.Person");

        // Act
        var up = await BuildAsync(fixture.Solution, employee, "up");
        var down = await BuildAsync(fixture.Solution, person, "down");

        // Assert
        Assert.Equal("class", up.Kind);
        Assert.Contains(up.Children, c => c.Name == "Person");
        Assert.Contains(down.Children, c => c.Name == "Employee");
    }

    // ── max-depth ──────────────────────────────────────────────────────────
    [Fact]
    public async Task MaxDepth1_TruncatesNodesWithFurtherChildren()
    {
        // Arrange
        using var fixture = CreateFixture();
        var animal = await ResolveTypeAsync(fixture.Solution, "Demo.Animal");

        // Act
        var root = await BuildAsync(fixture.Solution, animal, "down", maxDepth: 1);

        // Assert
        var dog = Single(root, "Dog");
        Assert.True(dog.Truncated);
        Assert.Empty(dog.Children); // Puppy elided
    }

    [Fact]
    public async Task NoMaxDepth_BuildsFullTreeWithoutTruncation()
    {
        // Arrange
        using var fixture = CreateFixture();
        var animal = await ResolveTypeAsync(fixture.Solution, "Demo.Animal");

        // Act
        var root = await BuildAsync(fixture.Solution, animal, "down");

        // Assert
        var dog = Single(root, "Dog");
        var puppy = Single(dog, "Puppy");
        Assert.False(dog.Truncated);
        Assert.False(puppy.Truncated);
    }

    // ── full path (UseCase): kind guard + group shaping ──────────────────────
    [Fact]
    public async Task ResolveAsync_EnumTarget_ThrowsInvalidTargetKind()
    {
        // Arrange
        using var fixture = CreateFixture();

        // Act
        var ex = await Assert.ThrowsAsync<DaemonValidationException>(() =>
            UseCase.ResolveAsync(fixture.Solution, "Demo.Color", null, null, null,
                direction: "down", includeFramework: false, maxDepth: null));

        // Assert
        Assert.Equal("INVALID_TARGET_KIND", ex.Error.Code);
    }

    [Fact]
    public async Task ResolveAsync_ClassTarget_ReturnsSingleGroupWithRootNode()
    {
        // Arrange
        using var fixture = CreateFixture();

        // Act
        var groups = await UseCase.ResolveAsync(fixture.Solution, "Demo.Animal", null, null, null,
            direction: "down", includeFramework: false, maxDepth: null);

        // Assert
        var group = Assert.Single(groups);
        Assert.Equal("Demo.Animal", group.Symbol);
        Assert.Equal("class", group.Kind);
        var root = Assert.IsType<HierarchyNode>(group.Result);
        Assert.Equal("Demo.Animal", root.FullName);
    }

    [Fact]
    public async Task ResolveAsync_InvalidDirection_ThrowsInvalidParams()
    {
        // Arrange
        using var fixture = CreateFixture();

        // Act
        var ex = await Assert.ThrowsAsync<DaemonValidationException>(() =>
            UseCase.ResolveAsync(fixture.Solution, "Demo.Animal", null, null, null,
                direction: "sideways", includeFramework: false, maxDepth: null));

        // Assert
        Assert.Equal("INVALID_PARAMS", ex.Error.Code);
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static Task<HierarchyNode> BuildAsync(
        Solution solution,
        INamedTypeSymbol type,
        string direction,
        bool includeFramework = false,
        int? maxDepth = null)
        => OutputMapping.BuildNodeAsync(
            solution, type, direction, includeFramework,
            maxDepth ?? HierarchyValidation.UnboundedMaxDepth, depth: 0, solutionDir: "", CancellationToken.None);

    private static HierarchyNode Single(HierarchyNode node, string childSimpleName)
        => Assert.Single(node.Children, c => c.Name == childSimpleName);

    private static async Task<INamedTypeSymbol> ResolveTypeAsync(Solution solution, string metadataName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            var type = compilation?.GetTypeByMetadataName(metadataName);
            if (type is not null)
                return type;
        }

        throw new InvalidOperationException($"Type not found: {metadataName}");
    }

    private const string MainSource = """
namespace Demo;

public class Animal { }
public class Dog : Animal { }
public class Puppy : Dog { }

public class MyError : System.Exception { }

public struct Coord { }

public record Person(string Name);
public record Employee(string Name, int Id) : Person(Name);

public interface IA { }
public interface IB : IA { }
public class CImpl : IA { }

public interface IBase { }
public interface ILeft : IBase { }
public interface IRight : IBase { }
public interface IDiamond : ILeft, IRight { }

public class Box<T> { }
public class StringBox : Box<string> { }

public enum Color { Red }
public delegate void Handler();
""";

    private static HierarchyFixture CreateFixture()
    {
        var workspace = CreateWorkspace();
        var projectId = ProjectId.CreateNewId(debugName: "Main");
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Main", "Main", LanguageNames.CSharp)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(projectId), "Main.cs", SourceText.From(MainSource),
                filePath: "/virtual/src/Main.cs");

        return new HierarchyFixture(workspace, solution);
    }

    private static HierarchyFixture CreateCrossProjectFixture()
    {
        var workspace = CreateWorkspace();
        var projectA = ProjectId.CreateNewId(debugName: "A");
        var projectB = ProjectId.CreateNewId(debugName: "B");

        var solution = workspace.CurrentSolution
            .AddProject(projectA, "A", "A", LanguageNames.CSharp)
            .AddMetadataReference(projectA, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(projectA), "Base.cs",
                SourceText.From("namespace Demo;\npublic class BaseType { }\n"), filePath: "/virtual/a/Base.cs")
            .AddProject(projectB, "B", "B", LanguageNames.CSharp)
            .AddMetadataReference(projectB, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddProjectReference(projectB, new ProjectReference(projectA))
            .AddDocument(DocumentId.CreateNewId(projectB), "Derived.cs",
                SourceText.From("namespace Demo;\npublic class DerivedType : BaseType { }\n"), filePath: "/virtual/b/Derived.cs");

        return new HierarchyFixture(workspace, solution);
    }

    private static AdhocWorkspace CreateWorkspace()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Concat([typeof(CSharpCompilation).Assembly])
            .Distinct();
        return new AdhocWorkspace(MefHostServices.Create(assemblies));
    }

    private sealed class HierarchyFixture(AdhocWorkspace workspace, Solution solution) : IDisposable
    {
        public AdhocWorkspace Workspace { get; } = workspace;
        public Solution Solution { get; } = solution;
        public void Dispose() => Workspace.Dispose();
    }
}
