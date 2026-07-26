using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

[Collection("Console output")]
public sealed class DaemonExecutableTests
{
    [Fact]
    public async Task RunAsync_WithoutArguments_WritesUsageMessage()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Act
        var exitCode = await DaemonExecutable.RunAsync([], stdout, stderr);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("This executable is for internal daemon/testing use. Use `dotnet aicraft`.\n", stdout.ToString().Replace("\r\n", "\n"));
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_CliStatus_WritesDaemonResponseJson()
    {
        // Arrange
        var solutionPath = Path.Combine(Path.GetTempPath(), $"dotnet-aicraft-daemon-cli-{Guid.NewGuid():N}.sln");
        await File.WriteAllTextAsync(solutionPath, string.Empty);

        await using var daemon = await SingleResponseDaemon.StartAsync(solutionPath, request =>
        {
            Assert.Equal("status", request.Command);
            return new DaemonResponse(
                Id: request.Id,
                Status: DaemonResponseStatus.Ok,
                Result: new { running = true, solutionPath = "/tmp/fake.sln" });
        });

        using var output = ConsoleOutputCapture.Start();
        var stderr = new StringWriter();

        // Act
        var exitCode = await DaemonExecutable.RunAsync(["cli", "--solution", solutionPath, "status"], TextWriter.Null, stderr);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());

        using var doc = JsonDocument.Parse(output.GetOutput());
        var root = doc.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("result").GetProperty("running").GetBoolean());
        Assert.Equal("/tmp/fake.sln", root.GetProperty("result").GetProperty("solutionPath").GetString());

        File.Delete(solutionPath);
    }

    private sealed class SingleResponseDaemon : IAsyncDisposable
    {
        private readonly Socket _listener;
        private readonly string _socketPath;
        private readonly Func<DaemonRequest, DaemonResponse> _handleRequest;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        private SingleResponseDaemon(Socket listener, string socketPath, Func<DaemonRequest, DaemonResponse> handleRequest)
        {
            _listener = listener;
            _socketPath = socketPath;
            _handleRequest = handleRequest;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public static Task<SingleResponseDaemon> StartAsync(string solutionPath, Func<DaemonRequest, DaemonResponse> handleRequest)
        {
            var socketPath = DaemonClient.GetSocketPath(solutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
            if (File.Exists(socketPath))
                File.Delete(socketPath);

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            socket.Listen(8);
            return Task.FromResult(new SingleResponseDaemon(socket, socketPath, handleRequest));
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    Socket accepted;
                    try
                    {
                        accepted = await _listener.AcceptAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    _ = Task.Run(() => HandleClientAsync(accepted));
                }
            }
            catch
            {
            }
        }

        private async Task HandleClientAsync(Socket socket)
        {
            try
            {
                await using var stream = new NetworkStream(socket, ownsSocket: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null)
                    return;

                var request = JsonOutput.Deserialize<DaemonRequest>(line)
                    ?? throw new InvalidOperationException("Failed to deserialize daemon request.");
                var response = _handleRequest(request);
                await writer.WriteLineAsync(JsonOutput.Serialize(response).AsMemory(), _cts.Token);
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Shutdown(SocketShutdown.Both); } catch { }
            _listener.Dispose();

            try { await _acceptLoop; } catch { }
            _cts.Dispose();

            try
            {
                if (File.Exists(_socketPath))
                    File.Delete(_socketPath);
            }
            catch
            {
            }
        }
    }
}
