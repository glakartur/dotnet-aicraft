using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.Build.Locator;
using Xunit;

namespace DotnetAICraft.Tests.Fixtures;

public sealed class SampleSolutionFixtureTests
{
    private static readonly string SolutionPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "fixtures",
        "SampleSolution",
        "SampleSolution.sln"));

    static SampleSolutionFixtureTests()
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

    [Fact]
    public void SampleSolutionFile_Exists()
    {
        // Arrange
        var solutionPath = SolutionPath;

        // Act
        var exists = File.Exists(solutionPath);

        // Assert
        Assert.True(exists, solutionPath);
    }

    [Fact]
    public async Task WorkspaceLoader_LoadsSampleSolutionProjects()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();

        // Act
        var projects = fixture.Solution.Projects;

        // Assert
        Assert.Contains(projects, p => p.Name == "Sample.Domain");
        Assert.Contains(projects, p => p.Name == "Sample.Infrastructure");
        Assert.Contains(projects, p => p.Name == "Sample.App");
        Assert.Contains(projects, p => p.Name == "Sample.Tests");
    }

    [Fact]
    public async Task Symbols_FindsTypesMembersAndEnumsInSampleSolution()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();

        // Act
        var page = await DotnetAICraft.Commands.Symbols.UseCase.ResolveAsync(
            fixture.Solution,
            "Order",
            kind: "all",
            limit: 50,
            offset: 0);

        // Assert
        Assert.Contains(page.Items, item => item.FullName == "Sample.Domain.Entities.Order" && item.Kind == "class");
        Assert.Contains(page.Items, item => item.FullName == "Sample.Domain.Services.OrderService" && item.Kind == "class");
        Assert.Contains(page.Items, item => item.FullName == "Sample.Domain.Entities.OrderStatus" && item.Kind == "enum");
    }

    [Fact]
    public async Task Refs_FindsCrossProjectReferencesToRepositoryInterface()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();

        // Act
        var groups = await DotnetAICraft.Commands.Refs.UseCase.ResolveAsync(
            fixture.Solution,
            "Sample.Domain.Repositories.IOrderRepository",
            file: null,
            line: null,
            col: null);

        // Assert
        var group = Assert.Single(groups);
        var refs = Assert.IsAssignableFrom<IReadOnlyList<ReferenceResult>>(group.Result);

        Assert.Contains(refs, r => r.File.EndsWith("Sample.Infrastructure/Persistence/InMemoryOrderRepository.cs", StringComparison.Ordinal));
        Assert.Contains(refs, r => r.File.EndsWith("Sample.App/Program.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Impls_FindsInterfaceImplementationsAcrossProjects()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();

        // Act
        var groups = await DotnetAICraft.Commands.Impls.UseCase.ResolveAsync(
            fixture.Solution,
            "Sample.Domain.Repositories.IOrderRepository");

        // Assert
        var group = Assert.Single(groups);
        var implementations = Assert.IsAssignableFrom<IReadOnlyList<SymbolResult>>(group.Result);

        Assert.Contains(implementations, item => item.FullName == "Sample.Infrastructure.Persistence.InMemoryOrderRepository");
    }

    [Fact]
    public async Task Hierarchy_FindsDerivedProcessorAcrossProjects()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();
        var processorFile = Path.Combine(
            Path.GetDirectoryName(SolutionPath)!,
            "src",
            "Sample.Domain",
            "Processing",
            "Processors.cs");

        // Act
        var groups = await DotnetAICraft.Commands.Hierarchy.UseCase.ResolveAsync(
            fixture.Solution,
            symbol: null,
            file: processorFile,
            line: 10,
            col: 23,
            direction: "down",
            includeFramework: false,
            maxDepth: 5);

        // Assert
        var group = Assert.Single(groups);
        var root = Assert.IsType<HierarchyNode>(group.Result);

        Assert.Contains(root.Children, child => child.FullName == "Sample.Domain.Processing.OrderProcessor");
        Assert.Contains(root.Children, child => child.FullName == "Sample.Infrastructure.Processing.AuditingOrderProcessor");
    }

    [Fact]
    public async Task Diagnostics_SampleSolutionHasNoErrors()
    {
        // Arrange
        await using var fixture = await LoadSampleSolutionAsync();

        // Act
        var diagnostics = await DotnetAICraft.Commands.Diagnostics.UseCase.ResolveAsync(
            fixture.Solution,
            severity: "error",
            project: null,
            file: null);

        // Assert
        Assert.Empty(diagnostics);
    }

    private static async Task<LoadedSampleSolution> LoadSampleSolutionAsync()
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(SolutionPath);
        return new LoadedSampleSolution(workspace, solution);
    }

    private sealed class LoadedSampleSolution(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, Microsoft.CodeAnalysis.Solution solution) : IAsyncDisposable
    {
        public Microsoft.CodeAnalysis.Solution Solution { get; } = solution;

        public ValueTask DisposeAsync()
        {
            workspace.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
