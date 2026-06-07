using DotnetAICraft.Roslyn;
using Microsoft.Build.Locator;
using Xunit;

namespace DotnetAICraft.Tests.Roslyn;

public class WorkspaceLoaderTests
{
    static WorkspaceLoaderTests()
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

    [Theory]
    [InlineData("/tmp/foo.txt")]
    [InlineData("/tmp/foo.json")]
    [InlineData("/tmp/foo")]
    public async Task LoadAsync_UnsupportedExtension_Throws(string path)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => WorkspaceLoader.LoadAsync(path));
        Assert.Contains(".slnx", ex.Message);
        Assert.Contains(".sln", ex.Message);
    }
}
