using System.Text.Json;
using DotnetAICraft.Tests.Support;
using ServerEntry = DotnetAICraft.Daemon.DaemonExecutable;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

[Collection("Console output")]
public class ServerStartEnsureRunningTests
{
    [Fact]
    public async Task StartAsync_WithInvalidIdleTimeout_ReturnsInvalidIdleTimeoutError()
    {
        // Arrange
        var solutionPath = CreateUniqueSolutionPath();
        string output;

        // Act
        using (var capture = ConsoleOutputCapture.Start())
        {
            await ServerEntry.RunAsync(["daemon", "--solution", solutionPath, "--idle-timeout", "garbage", "--format", "json"], TextWriter.Null, TextWriter.Null);
            output = capture.GetOutput();
        }

        // Assert
        using var json = JsonDocument.Parse(output);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("INVALID_IDLE_TIMEOUT", error.GetProperty("code").GetString());
    }

    private static string CreateUniqueSolutionPath()
        => Path.Combine(Path.GetTempPath(), $"dotnet-aicraft-ensurerun-{Guid.NewGuid():N}.sln");
}
