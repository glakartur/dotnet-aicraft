using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputDiagnosticsTests
{
    [Fact]
    public void NonEmpty_LabelThenRows_NoCountHeader()
    {
        var items = new[]
        {
            new DiagnosticResult("P", "CS0001", "error",   "msg1", "/a/F.cs", 1, 2, null, null),
            new DiagnosticResult("P", "CS1000", "warning", "msg3", "/a/F.cs", 5, 6, null, null),
            new DiagnosticResult("P", "INFO01", "info",    "msg6", "/a/F.cs", 11, 12, null, null)
        };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDiagnostics(items, "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("diagnostics:", lines[0]);
        Assert.Equal("error /a/F.cs:1:2 [CS0001]: msg1", lines[1]);
        Assert.Equal("warning /a/F.cs:5:6 [CS1000]: msg3", lines[2]);
        Assert.Equal("info /a/F.cs:11:12 [INFO01]: msg6", lines[3]);
        Assert.DoesNotContain("errors", cap.GetOutput());
        Assert.DoesNotContain("warnings", cap.GetOutput());
    }

    [Fact]
    public void Empty_LabelWithNoResults()
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDiagnostics(Array.Empty<DiagnosticResult>(), "S.sln");
        Assert.Equal($"diagnostics: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void ProjectLevel_NoFile_RendersProjectNameInsteadOfLocation()
    {
        var items = new[]
        {
            new DiagnosticResult("MyApp", "CS9999", "error", "Project broken", null, null, null, null, null),
        };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDiagnostics(items, "S.sln");
        Assert.Contains("error MyApp [CS9999]: Project broken", cap.GetOutput());
    }
}
