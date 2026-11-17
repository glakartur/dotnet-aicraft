using DotnetAICraft.Daemon.FileSetSync;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotnetAICraft.Tests.Daemon.FileSetSync;

public sealed class ProjectOwnershipResolverTests
{
    [Fact]
    public void Resolve_FileInSdkProjectDirectory_ReturnsOwnedByThatProject()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var file = t.PathOf("src/A/Models/Foo.cs");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(file);

        var owned = Assert.IsType<OwnershipDecision.Owned>(decision);
        Assert.Equal([projectId], owned.ProjectIds);
    }

    [Fact]
    public void Resolve_MultiTargetProject_ReturnsEveryProjectInstance()
    {
        using var t = new FileSetTestSolution();
        var first = t.AddProject("src/A/A.csproj", name: "A(net8.0)");
        var second = t.AddProjectInstance(t.PathOf("src/A/A.csproj"), "A(net10.0)");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        var owned = Assert.IsType<OwnershipDecision.Owned>(decision);
        Assert.Equal(new HashSet<ProjectId> { first, second }, owned.ProjectIds.ToHashSet());
    }

    [Fact]
    public void Resolve_NestedProjects_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.AddProject("src/A/Sub/Sub.csproj");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Sub/Foo.cs"));

        var fallback = Assert.IsType<OwnershipDecision.Fallback>(decision);
        Assert.Contains("overlapping projects", fallback.Reason);
    }

    [Fact]
    public void Resolve_NoOwnerAndAllProjectsSafe_ReturnsNotOwned()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("scripts/Tool.cs"));

        Assert.IsType<OwnershipDecision.NotOwned>(decision);
    }

    [Fact]
    public void Resolve_NoOwnerAndAProjectIncludesFilesFromOutside_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="..\Shared\*.cs" />
              </ItemGroup>
            </Project>
            """);

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/Shared/Common.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Fact]
    public void Resolve_OwnerSafeButAnotherProjectLinksFiles_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.AddProject("src/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><Compile Include="..\A\Shared.cs" Link="Shared.cs" /></ItemGroup>
            </Project>
            """);

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Shared.cs"));

        var fallback = Assert.IsType<OwnershipDecision.Fallback>(decision);
        Assert.Contains("B.csproj", fallback.Reason);
    }

    [Fact]
    public void Resolve_LegacyProject_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", FileSetTestSolution.LegacyProject);

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Theory]
    [InlineData("<ItemGroup><Compile Remove=\"Legacy/**\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Update=\"Foo.cs\" /></ItemGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>")]
    [InlineData("<PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);Legacy/**</DefaultItemExcludes></PropertyGroup>")]
    [InlineData("<PropertyGroup><DefaultExcludesInProjectFolder>Legacy/**</DefaultExcludesInProjectFolder></PropertyGroup>")]
    [InlineData("<PropertyGroup><BaseIntermediateOutputPath>build/obj/</BaseIntermediateOutputPath></PropertyGroup>")]
    [InlineData("<PropertyGroup><UseArtifactsOutput>true</UseArtifactsOutput></PropertyGroup>")]
    public void Resolve_ProjectWithCompilePolicyOverride_ReturnsFallback(string body)
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Theory]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk\" /></Project>")]
    [InlineData("<Project><Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" /><Import Project=\"Sdk.targets\" Sdk=\"Microsoft.NET.Sdk\" /></Project>")]
    public void Resolve_AlternativeSdkSyntax_ReturnsOwned(string projectXml)
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", projectXml);

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Owned>(decision);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void Resolve_DirectoryBuildFileDisablesDefaultCompileItems_ReturnsFallback(string fileName)
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.WriteFile(fileName, "<Project><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Fact]
    public void Resolve_DirectoryBuildPropsAboveSolutionDirectory_IsChecked()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("repo/src/A/A.csproj");
        // The solution directory is "repo"; the props file sits above it.
        t.WriteFile("Directory.Build.props", "<Project><PropertyGroup><DefaultItemExcludes>x/**</DefaultItemExcludes></PropertyGroup></Project>");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("repo/src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Fact]
    public void Resolve_DirectoryBuildPropsWithoutCompileOverrides_ReturnsOwned()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.WriteFile("Directory.Build.props", "<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Owned>(decision);
    }

    [Fact]
    public void Resolve_MalformedProjectXml_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/A/Foo.cs"));

        Assert.IsType<OwnershipDecision.Fallback>(decision);
    }

    [Fact]
    public void Resolve_OwnerIsNotACSharpProject_ReturnsNotOwned()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/Vb/Vb.vbproj");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/Vb/Foo.cs"));

        Assert.IsType<OwnershipDecision.NotOwned>(decision);
    }

    [Fact]
    public void Resolve_SiblingDirectoryWithCommonPrefix_IsNotOwned()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/Foo/Foo.csproj");

        var decision = new ProjectOwnershipResolver(t.Solution).Resolve(t.PathOf("src/FooBar/Bar.cs"));

        Assert.IsType<OwnershipDecision.NotOwned>(decision);
    }
}
