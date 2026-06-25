using DotnetAICraft.Commands.Hierarchy;
using DotnetAICraft.Daemon;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class HierarchyValidationTests
{
    [Theory]
    [InlineData("up", true, "up")]
    [InlineData("down", true, "down")]
    [InlineData("DOWN ", true, "down")]
    [InlineData(" Up", true, "up")]
    public void TryParseDirection_AcceptsUpDownCaseInsensitive(string? input, bool expectedOk, string expectedNormalized)
    {
        var ok = Validation.TryParseDirection(input, out var normalized, out var error);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedNormalized, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sideways")]
    [InlineData("incoming")]
    public void TryParseDirection_RejectsInvalid_WithAcceptedValuesDetail(string? input)
    {
        var ok = Validation.TryParseDirection(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("INVALID_PARAMS", error!.Code);
        Assert.NotNull(error.Details);
        Assert.Contains("up | down", System.Text.Json.JsonSerializer.Serialize(error.Details));
    }

    [Theory]
    [InlineData(null, true, Validation.UnboundedMaxDepth)]
    [InlineData(1, true, 1)]
    [InlineData(5, true, 5)]
    public void TryNormalizeMaxDepth_AcceptsNullAndPositive(int? input, bool expectedOk, int expectedNormalized)
    {
        var ok = Validation.TryNormalizeMaxDepth(input, out var normalized, out var error);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedNormalized, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void TryNormalizeMaxDepth_RejectsBelowOne(int input)
    {
        var ok = Validation.TryNormalizeMaxDepth(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("INVALID_PARAMS", error!.Code);
    }

    [Theory]
    [InlineData("Demo.Beast", true)]    // class
    [InlineData("Demo.Point", true)]    // struct
    [InlineData("Demo.IShape", true)]   // interface
    [InlineData("Demo.Rec", true)]      // record (surfaces as class)
    [InlineData("Demo.Color", false)]   // enum
    [InlineData("Demo.Handler", false)] // delegate
    public async Task EnsureTargetKind_AcceptsTypesRejectsOthers(string fullName, bool accepted)
    {
        var symbol = await ResolveTypeAsync(fullName);

        if (accepted)
        {
            var named = Validation.EnsureTargetKind(symbol);
            Assert.NotNull(named);
        }
        else
        {
            var ex = Assert.Throws<DaemonValidationException>(() => Validation.EnsureTargetKind(symbol));
            Assert.Equal("INVALID_TARGET_KIND", ex.Error.Code);
        }
    }

    [Fact]
    public async Task EnsureTargetKind_RejectsMethod()
    {
        var compilation = await CompileAsync();
        var beast = compilation.GetTypeByMetadataName("Demo.Beast")!;
        var method = beast.GetMembers("Roar").Single();

        var ex = Assert.Throws<DaemonValidationException>(() => Validation.EnsureTargetKind(method));
        Assert.Equal("INVALID_TARGET_KIND", ex.Error.Code);
    }

    private static async Task<ISymbol> ResolveTypeAsync(string metadataName)
    {
        var compilation = await CompileAsync();
        return compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Type not found: {metadataName}");
    }

    private static async Task<Compilation> CompileAsync()
    {
        var host = MefHostServices.Create(
            MefHostServices.DefaultAssemblies.Concat([typeof(CSharpCompilation).Assembly]).Distinct());
        using var workspace = new AdhocWorkspace(host);
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "P", "P", LanguageNames.CSharp)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(projectId), "Kinds.cs", SourceText.From("""
namespace Demo;

public class Beast { public void Roar() { } }
public struct Point { }
public interface IShape { }
public record Rec(int X);
public enum Color { Red }
public delegate void Handler();
"""), filePath: "/virtual/Kinds.cs");

        var compilation = await solution.GetProject(projectId)!.GetCompilationAsync();
        return compilation!;
    }
}
