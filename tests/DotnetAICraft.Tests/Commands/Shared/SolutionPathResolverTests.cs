using System.Runtime.InteropServices;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Commands.Shared;

[Collection("Console output")]
public class SolutionPathResolverTests : IDisposable
{
    private readonly string _tempDir;

    public SolutionPathResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "spr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void Resolve_BothFlagsDifferentPaths_WritesConflictAndReturnsNull()
    {
        var a = Touch("A.sln");
        var b = Touch("B.csproj");

        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(new FileInfo(a), new FileInfo(b), OutputFormat.Json, _tempDir);

        Assert.Null(result);
        Assert.Contains("CONFLICTING_PATH_ARGUMENTS", cap.GetOutput());
    }

    [Fact]
    public void Resolve_BothFlagsSamePath_ReturnsNormalizedFullPath()
    {
        var p = Touch("App.sln");
        var alt = new FileInfo(Path.Combine(_tempDir, ".", "App.sln"));

        var result = SolutionPathResolver.Resolve(new FileInfo(p), alt, OutputFormat.Text, _tempDir);

        Assert.Equal(Path.GetFullPath(p), result);
    }

    [Fact]
    public void Resolve_OnlySolutionProvided_ReturnsItsFullPath()
    {
        var p = Touch("App.sln");
        var result = SolutionPathResolver.Resolve(new FileInfo(p), null, OutputFormat.Text, _tempDir);
        Assert.Equal(Path.GetFullPath(p), result);
    }

    [Fact]
    public void Resolve_OnlyProjectProvided_ReturnsItsFullPath()
    {
        var p = Touch("App.csproj");
        var result = SolutionPathResolver.Resolve(null, new FileInfo(p), OutputFormat.Text, _tempDir);
        Assert.Equal(Path.GetFullPath(p), result);
    }

    [Fact]
    public void Resolve_NoFlags_SlnxTakesPriorityOverSlnAndCsproj()
    {
        var slnx = Touch("App.slnx");
        Touch("App.sln");
        Touch("App.csproj");

        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Text, _tempDir);
        Assert.Equal(Path.GetFullPath(slnx), result);
    }

    [Fact]
    public void Resolve_NoFlags_SlnBeatsCsproj()
    {
        var sln = Touch("App.sln");
        Touch("App.csproj");

        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Text, _tempDir);
        Assert.Equal(Path.GetFullPath(sln), result);
    }

    [Fact]
    public void Resolve_NoFlags_CsprojOnly_ReturnsIt()
    {
        var p = Touch("App.csproj");
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Text, _tempDir);
        Assert.Equal(Path.GetFullPath(p), result);
    }

    [Fact]
    public void Resolve_NoFlags_TwoSlnFiles_AmbiguousError()
    {
        Touch("A.sln");
        Touch("B.sln");

        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Json, _tempDir);

        Assert.Null(result);
        var output = cap.GetOutput();
        Assert.Contains("SOLUTION_AMBIGUOUS", output);
        Assert.Contains("A.sln", output);
        Assert.Contains("B.sln", output);
    }

    [Fact]
    public void Resolve_NoFlags_AmbiguousOnFirstTier_DoesNotFallThrough()
    {
        Touch("A.slnx");
        Touch("B.slnx");
        Touch("Fallback.sln");

        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Json, _tempDir);

        Assert.Null(result);
        Assert.Contains("SOLUTION_AMBIGUOUS", cap.GetOutput());
        Assert.Contains("*.slnx", cap.GetOutput());
    }

    [Fact]
    public void Resolve_NoFlags_EmptyDir_NotFound()
    {
        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Json, _tempDir);

        Assert.Null(result);
        Assert.Contains("SOLUTION_NOT_FOUND", cap.GetOutput());
    }

    [Fact]
    public void Resolve_NoFlags_OnlyVbprojPresent_NotFound()
    {
        Touch("App.vbproj");

        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Json, _tempDir);

        Assert.Null(result);
        Assert.Contains("SOLUTION_NOT_FOUND", cap.GetOutput());
    }

    [Fact]
    public void Resolve_ErrorEnvelopeUsesTextFormat()
    {
        using var cap = ConsoleOutputCapture.Start();
        var result = SolutionPathResolver.Resolve(null, null, OutputFormat.Text, _tempDir);

        Assert.Null(result);
        // Text envelope, not JSON
        var output = cap.GetOutput();
        Assert.Contains("SOLUTION_NOT_FOUND", output);
        Assert.DoesNotContain("\"code\"", output);
    }

    [Fact]
    public void Resolve_WindowsCaseInsensitive_SamePathDifferentCase_NoConflict()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var p = Touch("App.sln");
        var upper = new FileInfo(p.ToUpperInvariant());
        var lower = new FileInfo(p.ToLowerInvariant());

        var result = SolutionPathResolver.Resolve(upper, lower, OutputFormat.Text, _tempDir);
        Assert.NotNull(result);
    }
}
