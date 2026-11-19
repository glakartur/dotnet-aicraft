using DotnetAICraft.Daemon;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public sealed class MetadataReloadCandidateTests
{
    [Theory]
    [InlineData("/repo/App.slnx")]
    [InlineData("/repo/App.sln")]
    [InlineData("/repo/src/A/A.csproj")]
    [InlineData("/repo/src/B/B.vbproj")]
    [InlineData("/repo/src/C/C.fsproj")]
    [InlineData("/repo/Directory.Build.props")]
    [InlineData("/repo/Directory.Build.targets")]
    [InlineData("/repo/Directory.Packages.props")]
    [InlineData("/repo/directory.packages.props")]
    public void IsMetadataReloadCandidate_MetadataFile_ReturnsTrue(string path)
        => Assert.True(DaemonServer.IsMetadataReloadCandidate(path));

    [Theory]
    [InlineData("/repo/src/A/Foo.cs")]
    [InlineData("/repo/Directory.Packages.props.bak")]
    [InlineData("/repo/notes.json")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMetadataReloadCandidate_OtherPath_ReturnsFalse(string? path)
        => Assert.False(DaemonServer.IsMetadataReloadCandidate(path));
}
