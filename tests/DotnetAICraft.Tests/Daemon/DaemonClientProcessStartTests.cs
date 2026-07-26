using System.Diagnostics;
using DotnetAICraft.Daemon;
using DotnetAICraft.Diagnostics;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public sealed class DaemonClientProcessStartTests
{
    [Fact]
    public void ApplySpawnedDaemonEnvironment_DoesNotSetDotnetAicraftDebug_EvenWhenGlobalVerboseEnabled()
    {
        var wasEnabled = DebugLog.IsEnabled;
        DebugLog.Configure(true);
        try
        {
            // Arrange
            var startInfo = DaemonClient.CreateDaemonStartInfo("aicraft-daemon", ["daemon"]);
            startInfo.EnvironmentVariables["DOTNET_AICRAFT_DEBUG"] = "1";

            // Act
            DaemonClient.ApplySpawnedDaemonEnvironment(startInfo);

            // Assert
            Assert.False(startInfo.EnvironmentVariables.ContainsKey("DOTNET_AICRAFT_DEBUG"));
        }
        finally
        {
            DebugLog.Configure(wasEnabled);
        }
    }

    [Fact]
    public void ApplySpawnedDaemonEnvironment_StripsInheritedDotnetAicraftDebug()
    {
        // Arrange
        var startInfo = DaemonClient.CreateDaemonStartInfo("aicraft-daemon", ["daemon"]);
        startInfo.EnvironmentVariables["DOTNET_AICRAFT_DEBUG"] = "1";

        // Act
        DaemonClient.ApplySpawnedDaemonEnvironment(startInfo);

        // Assert
        Assert.False(startInfo.EnvironmentVariables.ContainsKey("DOTNET_AICRAFT_DEBUG"));
    }

    [Fact]
    public void CreateDaemonStartInfo_ConfiguresSafeNonInheritingDefaults()
    {
        // Arrange
        var executablePath = "aicraft-daemon";
        var args = new[] { "daemon" };

        // Act
        var startInfo = DaemonClient.CreateDaemonStartInfo(executablePath, args);

        // Assert
        Assert.Equal("aicraft-daemon", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void CreateDaemonStartInfo_PreservesArgumentOrder()
    {
        // Arrange
        var args = new[] { "daemon", "--solution", "/tmp/sample.sln", "--idle-timeout", "off" };

        // Act
        var startInfo = DaemonClient.CreateDaemonStartInfo("aicraft-daemon", args);

        // Assert
        Assert.Equal(args.Length, startInfo.ArgumentList.Count);
        for (var i = 0; i < args.Length; i++)
            Assert.Equal(args[i], startInfo.ArgumentList[i]);
    }

    [Fact]
    public void ResolveDaemonExecutablePath_PrefersAppBaseDaemonExecutable()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory("aicraft-daemon-path-test-");
        try
        {
            var shimDir = Path.Combine(tempDir.FullName, "shims");
            var appBaseDir = Path.Combine(tempDir.FullName, "store", "tools", "net10.0", "any");
            Directory.CreateDirectory(shimDir);
            Directory.CreateDirectory(appBaseDir);

            var currentProcessPath = Path.Combine(shimDir, OperatingSystem.IsWindows() ? "dotnet-aicraft.exe" : "dotnet-aicraft");
            var packagedDaemonPath = Path.Combine(appBaseDir, OperatingSystem.IsWindows() ? "aicraft-daemon.exe" : "aicraft-daemon");

            File.WriteAllText(currentProcessPath, string.Empty);
            File.WriteAllText(packagedDaemonPath, string.Empty);

            // Act
            var resolved = DaemonClient.ResolveDaemonExecutablePath(currentProcessPath, appBaseDir);

            // Assert
            Assert.Equal(packagedDaemonPath, resolved);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveDaemonLaunchCommand_FallsBackToDotnetDll_WhenNativeExecutableMissing()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory("aicraft-daemon-path-test-");
        try
        {
            var shimDir = Path.Combine(tempDir.FullName, "shims");
            var appBaseDir = Path.Combine(tempDir.FullName, "store", "tools", "net10.0", "any");
            Directory.CreateDirectory(shimDir);
            Directory.CreateDirectory(appBaseDir);

            var currentProcessPath = Path.Combine(shimDir, OperatingSystem.IsWindows() ? "dotnet-aicraft.exe" : "dotnet-aicraft");
            var daemonDllPath = Path.Combine(appBaseDir, "aicraft-daemon.dll");

            File.WriteAllText(currentProcessPath, string.Empty);
            File.WriteAllText(daemonDllPath, string.Empty);

            // Act
            var launchCommand = DaemonClient.ResolveDaemonLaunchCommand(currentProcessPath, appBaseDir);

            // Assert
            Assert.Equal(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", launchCommand.FileName);
            Assert.Equal([daemonDllPath], launchCommand.ArgumentPrefix);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveDaemonExecutablePath_PrefersSiblingDaemonExecutable()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory("aicraft-daemon-path-test-");
        try
        {
            var currentProcessPath = Path.Combine(tempDir.FullName, OperatingSystem.IsWindows() ? "dotnet-aicraft.exe" : "dotnet-aicraft");
            var siblingDaemonPath = Path.Combine(tempDir.FullName, OperatingSystem.IsWindows() ? "aicraft-daemon.exe" : "aicraft-daemon");

            File.WriteAllText(currentProcessPath, string.Empty);
            File.WriteAllText(siblingDaemonPath, string.Empty);

            // Act
            var resolved = DaemonClient.ResolveDaemonExecutablePath(currentProcessPath);

            // Assert
            Assert.Equal(siblingDaemonPath, resolved);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveDaemonExecutablePath_FallsBackToCommandName_WhenSiblingMissing()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory("aicraft-daemon-path-test-");
        try
        {
            var currentProcessPath = Path.Combine(tempDir.FullName, OperatingSystem.IsWindows() ? "dotnet-aicraft.exe" : "dotnet-aicraft");
            File.WriteAllText(currentProcessPath, string.Empty);

            // Act
            var resolved = DaemonClient.ResolveDaemonExecutablePath(currentProcessPath);

            // Assert
            Assert.Equal(OperatingSystem.IsWindows() ? "aicraft-daemon.exe" : "aicraft-daemon", resolved);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DrainProcessPipeAsync_CompletesAtEndOfStream()
    {
        // Arrange
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("echo drained");
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("printf 'drained\\n'");
        }

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        // Act
        var drainTask = DaemonClient.DrainProcessPipeAsync(process!.StandardOutput);
        await process.WaitForExitAsync();
        await drainTask;

        // Assert
        Assert.True(drainTask.IsCompletedSuccessfully);
    }
}
