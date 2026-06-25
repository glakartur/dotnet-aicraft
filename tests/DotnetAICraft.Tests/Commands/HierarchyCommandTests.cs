using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

public class HierarchyCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptions()
    {
        var command = HierarchyCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption(), formatOption: BuildFormatOption());

        Assert.Equal("hierarchy", command.Name);
        AssertContainsOption(command, "--solution");
        AssertContainsOption(command, "--symbol");
        AssertContainsOption(command, "--file");
        AssertContainsOption(command, "--line");
        AssertContainsOption(command, "--col");
        AssertContainsOption(command, "--direction");
        AssertContainsOption(command, "--include-framework");
        AssertContainsOption(command, "--max-depth");
        AssertContainsOption(command, "--idle-timeout");
        AssertContainsOption(command, "--format");
    }

    [Fact]
    public void Parse_MissingDirection_ProducesError()
    {
        var command = HierarchyCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        var parseResult = command.Parse([
            "--solution", "/tmp/sample.sln",
            "--symbol", "Demo.Animal"]);

        Assert.NotEmpty(parseResult.Errors);
    }

    [Fact]
    public void Parse_WithDirection_NoErrors_AndDefaults()
    {
        var command = HierarchyCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        var parseResult = command.Parse([
            "--solution", "/tmp/sample.sln",
            "--symbol", "Demo.Animal",
            "--direction", "down"]);

        var directionOption = GetOption<string>(command, "--direction");
        var includeFrameworkOption = GetOption<bool>(command, "--include-framework");
        var maxDepthOption = GetOption<int?>(command, "--max-depth");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("down", parseResult.GetValue(directionOption));
        Assert.False(parseResult.GetValue(includeFrameworkOption));
        Assert.Null(parseResult.GetValue(maxDepthOption));
    }

    [Fact]
    public void Parse_WithFlags_ReadsValues()
    {
        var command = HierarchyCommand.Build(BuildSolutionOption(), BuildProjectOption(), BuildIdleTimeoutOption());

        var parseResult = command.Parse([
            "--solution", "/tmp/sample.sln",
            "--symbol", "Demo.Animal",
            "--direction", "up",
            "--include-framework",
            "--max-depth", "3"]);

        Assert.Empty(parseResult.Errors);
        Assert.True(parseResult.GetValue(GetOption<bool>(command, "--include-framework")));
        Assert.Equal(3, parseResult.GetValue(GetOption<int?>(command, "--max-depth")));
    }

    private static Option<OutputFormat> BuildFormatOption()
        => new("--format") { DefaultValueFactory = _ => OutputFormat.Text };

    private static Option<FileInfo> BuildSolutionOption()
        => new("--solution", "-s") { Required = false };

    private static Option<FileInfo> BuildProjectOption()
        => new("--project", "-p") { Required = false };

    private static Option<string?> BuildIdleTimeoutOption()
        => new("--idle-timeout");

    private static Option<T> GetOption<T>(Command command, string alias)
        => Assert.IsType<Option<T>>(command.Options.Single(opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias)));

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
