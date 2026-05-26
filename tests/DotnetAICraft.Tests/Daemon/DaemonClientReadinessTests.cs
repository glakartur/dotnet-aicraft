using System.Net.Sockets;
using System.Text;
using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

[Collection("Console output")]
public sealed class DaemonClientReadinessTests
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task ReadinessWait_FirstPollLoaded_ReturnsClientAndEmitsNoNotices()
    {
        var solutionPath = CreateUniqueSolutionPath();
        await using var fake = await FakeStatusDaemon.StartAsync(solutionPath, "loaded");

        using var capture = ConsoleOutputCapture.Start();

        var client = await DaemonClient.ConnectOrStartCoreAsync(
            () => ReservedOutcomeAsync(solutionPath),
            (outcome, ct) => DaemonClient.WaitForSolutionReadyAsync(solutionPath, outcome, ReadyTimeout, FastPollInterval, ct),
            firstNoticeAfter: TimeSpan.FromMilliseconds(500),
            secondNoticeAfter: TimeSpan.FromMilliseconds(1000));

        await using (client)
        {
            var output = capture.GetOutput();
            Assert.DoesNotContain(DaemonClient.FirstNoticeMessage, output, StringComparison.Ordinal);
            Assert.DoesNotContain(DaemonClient.SecondNoticeMessage, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReadinessWait_LoadingPastBothThresholds_EmitsBothNoticesOnceInOrder()
    {
        var solutionPath = CreateUniqueSolutionPath();
        // Report "loading" for several polls before "loaded" so the wait reliably
        // crosses both injected notice thresholds.
        var states = Enumerable.Repeat("loading", 8).Append("loaded").ToArray();
        await using var fake = await FakeStatusDaemon.StartAsync(solutionPath, states);

        using var capture = ConsoleOutputCapture.Start();

        var client = await DaemonClient.ConnectOrStartCoreAsync(
            () => ReservedOutcomeAsync(solutionPath),
            (outcome, ct) => DaemonClient.WaitForSolutionReadyAsync(solutionPath, outcome, ReadyTimeout, TimeSpan.FromMilliseconds(25), ct),
            firstNoticeAfter: TimeSpan.FromMilliseconds(15),
            secondNoticeAfter: TimeSpan.FromMilliseconds(40));

        await using (client)
        {
            var output = capture.GetOutput();

            var firstIndex = output.IndexOf(DaemonClient.FirstNoticeMessage, StringComparison.Ordinal);
            var secondIndex = output.IndexOf(DaemonClient.SecondNoticeMessage, StringComparison.Ordinal);

            Assert.True(firstIndex >= 0, "First notice should have been emitted.");
            Assert.True(secondIndex >= 0, "Second notice should have been emitted.");
            Assert.True(firstIndex < secondIndex, "First notice must precede the second.");

            Assert.Equal(1, CountOccurrences(output, DaemonClient.FirstNoticeMessage));
            Assert.Equal(1, CountOccurrences(output, DaemonClient.SecondNoticeMessage));
        }
    }

    [Fact]
    public async Task ReadinessWait_LoadedAfterLoading_ReturnsUnusedClientThatCanRunCommand()
    {
        var solutionPath = CreateUniqueSolutionPath();
        await using var fake = await FakeStatusDaemon.StartAsync(solutionPath, "loading", "loaded");

        var client = await DaemonClient.ConnectOrStartCoreAsync(
            () => ReservedOutcomeAsync(solutionPath),
            (outcome, ct) => DaemonClient.WaitForSolutionReadyAsync(solutionPath, outcome, ReadyTimeout, FastPollInterval, ct),
            firstNoticeAfter: TimeSpan.FromMilliseconds(500),
            secondNoticeAfter: TimeSpan.FromMilliseconds(1000));

        await using (client)
        {
            // The returned client is the reserved, never-spent connection: its
            // first real request must succeed (regression guard against returning
            // a connection already consumed by a status poll).
            var response = await client.SendAsync("status");
            Assert.Equal(DaemonResponseStatus.Ok, response.Status);
        }
    }

    [Fact]
    public async Task ReadinessWait_UnloadedWithLoadError_ThrowsSurfacingDaemonError()
    {
        var solutionPath = CreateUniqueSolutionPath();
        await using var fake = await FakeStatusDaemon.StartAsync(
            solutionPath,
            new StatusReply("unloaded", "SOLUTION_LOAD_FAILED", "Failed to load solution."));

        var ex = await Assert.ThrowsAsync<DaemonClientValidationException>(() =>
            DaemonClient.ConnectOrStartCoreAsync(
                () => ReservedOutcomeAsync(solutionPath),
                (outcome, ct) => DaemonClient.WaitForSolutionReadyAsync(solutionPath, outcome, ReadyTimeout, FastPollInterval, ct),
                firstNoticeAfter: TimeSpan.FromMilliseconds(500),
                secondNoticeAfter: TimeSpan.FromMilliseconds(1000)));

        Assert.Equal("SOLUTION_LOAD_FAILED", ex.Error.Code);
    }

    [Fact]
    public async Task ReadinessWait_NeverLoadsWithinTimeout_ThrowsReadyTimeout()
    {
        var solutionPath = CreateUniqueSolutionPath();
        await using var fake = await FakeStatusDaemon.StartAsync(solutionPath, "loading");

        var ex = await Assert.ThrowsAsync<DaemonClientValidationException>(() =>
            DaemonClient.ConnectOrStartCoreAsync(
                () => ReservedOutcomeAsync(solutionPath),
                (outcome, ct) => DaemonClient.WaitForSolutionReadyAsync(solutionPath, outcome, TimeSpan.FromMilliseconds(120), FastPollInterval, ct),
                firstNoticeAfter: TimeSpan.FromMilliseconds(500),
                secondNoticeAfter: TimeSpan.FromMilliseconds(1000)));

        Assert.Equal("DAEMON_STARTUP_READY_TIMEOUT", ex.Error.Code);
    }

    private static async Task<DaemonStartupOutcome> ReservedOutcomeAsync(string solutionPath)
    {
        var reserved = await DaemonClient.TryConnectAsync(solutionPath)
            ?? throw new InvalidOperationException("Fake daemon was not reachable.");
        return DaemonStartupOutcome.Attached(reserved);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string CreateUniqueSolutionPath()
        => Path.Combine(Path.GetTempPath(), $"dotnet-aicraft-test-{Guid.NewGuid():N}.sln");

    private readonly record struct StatusReply(string LoadState, string? ErrorCode = null, string? ErrorMessage = null);

    /// <summary>
    /// A minimal stand-in for the daemon that answers <c>status</c> with a scripted
    /// sequence of load states (the last entry repeats). One request per connection,
    /// mirroring the real daemon's connection model so the readiness poll reconnects
    /// per iteration.
    /// </summary>
    private sealed class FakeStatusDaemon : IAsyncDisposable
    {
        private readonly Socket _listener;
        private readonly string _socketPath;
        private readonly StatusReply[] _replies;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private int _requestIndex = -1;

        private FakeStatusDaemon(Socket listener, string socketPath, StatusReply[] replies)
        {
            _listener = listener;
            _socketPath = socketPath;
            _replies = replies;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public static Task<FakeStatusDaemon> StartAsync(string solutionPath, params string[] loadStates)
            => StartAsync(solutionPath, loadStates.Select(s => new StatusReply(s)).ToArray());

        public static Task<FakeStatusDaemon> StartAsync(string solutionPath, params StatusReply[] replies)
        {
            var socketPath = DaemonClient.GetSocketPath(solutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
            if (File.Exists(socketPath))
                File.Delete(socketPath);

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            socket.Listen(16);
            return Task.FromResult(new FakeStatusDaemon(socket, socketPath, replies));
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
                // The test asserts on observed client behavior, not listener health.
            }
        }

        private async Task HandleClientAsync(Socket socket)
        {
            try
            {
                await using var stream = new NetworkStream(socket, ownsSocket: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                // Blocks until a request arrives; the reserved (unused) connection
                // never sends, so it never consumes a scripted reply.
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null)
                    return;

                var index = Interlocked.Increment(ref _requestIndex);
                var reply = _replies[Math.Min(index, _replies.Length - 1)];

                var response = new DaemonResponse(
                    Id: Guid.NewGuid().ToString("N"),
                    Status: DaemonResponseStatus.Ok,
                    Result: new
                    {
                        running = true,
                        loadState = reply.LoadState,
                        lastLoadErrorCode = reply.ErrorCode,
                        lastLoadErrorMessage = reply.ErrorMessage
                    });

                await writer.WriteLineAsync(JsonOutput.Serialize(response).AsMemory(), _cts.Token);
            }
            catch
            {
                // Ignore IO/cancellation; the test depends on collected behavior.
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
            catch { }
        }
    }
}
