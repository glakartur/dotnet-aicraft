using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

public class SymbolsCommandTests
{
    [Fact]
    public void Build_ExposesPaginationOptionsWithDefaultValues()
    {
        // Arrange
        var command = SymbolsCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption(), formatOption: BuildFormatOption());
        var limitOption = GetOption<int>(command, "--limit");
        var offsetOption = GetOption<int>(command, "--offset");

        // Act
        var parseResult = command.Parse([
            "--solution", "/tmp/sample.sln",
            "--pattern", "Pagination*"]);

        // Assert
        Assert.Equal("symbols", command.Name);
        AssertContainsOption(command, "--limit");
        AssertContainsOption(command, "--offset");
        AssertContainsOption(command, "--format");

        Assert.Empty(parseResult.Errors);
        Assert.Equal(AnalysisCommandMetadata.SymbolsDefaultLimit, parseResult.GetValue(limitOption));
        Assert.Equal(AnalysisCommandMetadata.SymbolsDefaultOffset, parseResult.GetValue(offsetOption));
    }

    [Fact]
    public void Build_KindOptionDescription_ListsGranularKinds()
    {
        // Arrange
        var command = SymbolsCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        // Act
        var kindOption = GetOption<string>(command, "--kind");

        // Assert
        Assert.Contains("constructor", kindOption.Description, StringComparison.Ordinal);
        Assert.Contains("interface", kindOption.Description, StringComparison.Ordinal);
        Assert.Contains("event", kindOption.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UsesProvidedLimitAndOffsetValues()
    {
        // Arrange
        var command = SymbolsCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());
        var limitOption = GetOption<int>(command, "--limit");
        var offsetOption = GetOption<int>(command, "--offset");

        // Act
        var parseResult = command.Parse([
            "--solution", "/tmp/sample.sln",
            "--pattern", "Pagination*",
            "--limit", "1",
            "--offset", "50"]);

        // Assert
        Assert.Empty(parseResult.Errors);
        Assert.Equal(1, parseResult.GetValue(limitOption));
        Assert.Equal(50, parseResult.GetValue(offsetOption));
    }

    [Fact]
    public void Parse_NoPathFlags_DoesNotError()
    {
        // Arrange
        var command = SymbolsCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        // Act
        var parseResult = command.Parse(["--pattern", "Foo*"]);

        // Assert
        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void Parse_BothSolutionAndProjectFlags_ParseSuccessfully()
    {
        // Arrange
        var command = SymbolsCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        // Act
        var parseResult = command.Parse([
            "--solution", "/tmp/a.sln",
            "--project", "/tmp/b.csproj",
            "--pattern", "Foo*"]);

        // Assert
        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void Build_ExposesProjectOptionAndAlias()
    {
        // Arrange
        var solutionOption = BuildSolutionOption();
        var projectOption = BuildProjectOption();
        var idleTimeoutOption = BuildIdleTimeoutOption();

        // Act
        var command = SymbolsCommand.Build(solutionOption, projectOption, idleTimeoutOption);

        // Assert
        AssertContainsOption(command, "--project");
        AssertContainsOption(command, "-p");
    }

    private static Option<FileInfo> BuildSolutionOption()
        => new("--solution", "-s") { Required = false };

    private static Option<FileInfo> BuildProjectOption()
        => new("--project", "-p") { Required = false };

    private static Option<string?> BuildIdleTimeoutOption()
        => new("--idle-timeout");

    private static Option<OutputFormat> BuildFormatOption()
        => new("--format") { DefaultValueFactory = _ => OutputFormat.Text };

    private static Option<T> GetOption<T>(Command command, string alias)
        => Assert.IsType<Option<T>>(command.Options.Single(opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias)));

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
