using System.Text.Json;
using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// A copy of tests/fixtures/SampleSolution in a temp directory, loaded by a
/// <see cref="DaemonServer"/> with live file watchers (no socket). Requests go
/// through <see cref="DaemonServer.DispatchAsync"/>.
/// </summary>
internal sealed class LiveSampleSolution : IAsyncDisposable
{
    public const string TraitName = "Category";
    public const string TraitValue = "FileWatcherIntegration";

    private static readonly string FixtureRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "SampleSolution"));

    private LiveSampleSolution(string root, DaemonServer server)
    {
        Root = root;
        Server = server;
    }

    static LiveSampleSolution()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            var instance = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(i => i.Version)
                .FirstOrDefault();
            if (instance is not null)
                MSBuildLocator.RegisterInstance(instance);
        }
    }

    public string Root { get; }

    public DaemonServer Server { get; }

    public Solution Solution => Server.CurrentSolutionForTesting
        ?? throw new InvalidOperationException("Solution is not loaded.");

    /// <param name="prepare">Changes the copied files before the daemon starts.</param>
    /// <param name="serverFactory">Creates the server for the solution path (to set timing options).</param>
    public static async Task<LiveSampleSolution> StartAsync(
        Action<string>? prepare = null,
        Func<string, DaemonServer>? serverFactory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "aicraft-live-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(FixtureRoot, root);
        prepare?.Invoke(root);

        var solutionPath = Path.Combine(root, "SampleSolution.sln");
        var server = serverFactory?.Invoke(solutionPath) ?? new DaemonServer(solutionPath);
        var live = new LiveSampleSolution(root, server);
        server.StartWorkspaceServices();
        await live.WaitLoadedAsync(expectedAttempts: 1);
        return live;
    }

    public string PathOf(string relativePath)
        => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string WriteFile(string relativePath, string content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public async Task WaitLoadedAsync(int expectedAttempts)
    {
        await EventuallyAsync(() => Server.LoadAttemptCount >= expectedAttempts, TimeSpan.FromSeconds(120));
        await Server.GetBackgroundLoadTaskForTesting();
        var status = await StatusAsync();
        if (status.LoadState != "loaded")
            throw new InvalidOperationException($"Solution did not load: {status.LastLoadErrorMessage}");
    }

    public async Task<DaemonResponse<object?>> SendAsync(string command, object? parameters = null)
        => await Server.DispatchAsync(new DaemonRequest(Guid.NewGuid().ToString("N"), command, parameters), CancellationToken.None);

    public async Task<DaemonStatus> StatusAsync()
        => (DaemonStatus)(await SendAsync("status")).Result!;

    public async Task<IReadOnlyList<SymbolResult>> SymbolsAsync(string pattern)
    {
        var response = await SendAsync("symbols", new { pattern });
        return response.Result is IReadOnlyList<SymbolResult> items ? items : [];
    }

    public async Task<JsonElement> ResultJsonAsync(string command, object? parameters = null)
    {
        var response = await SendAsync(command, parameters);
        return JsonDocument.Parse(JsonOutput.Serialize(response)).RootElement.Clone();
    }

    public IReadOnlyList<Document> DocumentsAt(string path)
        => Solution.Projects.SelectMany(p => p.Documents)
            .Where(d => string.Equals(d.FilePath, path, StringComparison.Ordinal))
            .ToList();

    public static async Task EventuallyAsync(Func<bool> condition, TimeSpan? timeout = null)
        => await EventuallyAsync(() => Task.FromResult(condition()), timeout);

    public static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met before timeout.");
            await Task.Delay(50);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        try { Directory.Delete(Root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "obj" or "bin")
                continue;
            CopyDirectory(directory, Path.Combine(target, name));
        }
    }
}
