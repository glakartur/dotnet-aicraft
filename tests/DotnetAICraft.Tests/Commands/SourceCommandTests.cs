using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Source;
using DotnetAICraft.Output;
using Xunit;
using SourceCliValidation = DotnetAICraft.Commands.Source.CliValidation;

namespace DotnetAICraft.Tests.Commands;

public class SourceCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptions()
    {
        // Arrange
        var solutionOption = new Option<FileInfo>("--solution", "-s") { Required = false };
        var projectOption = new Option<FileInfo>("--project", "-p") { Required = false };
        var idleTimeoutOption = new Option<string?>("--idle-timeout");
        var formatOption = new Option<OutputFormat>("--format") { DefaultValueFactory = _ => OutputFormat.Text };

        // Act
        var command = SourceCommand.Build(
            solutionOption,
            projectOption,
            idleTimeoutOption,
            formatOption: formatOption);

        // Assert
        Assert.Equal("source", command.Name);
        AssertContainsOption(command, "--file");
        AssertContainsOption(command, "--line");
        AssertContainsOption(command, "--col");
        AssertContainsOption(command, "--symbol");
        AssertContainsOption(command, "--format");
    }

    [Fact]
    public void ValidateArgs_RejectsMissingMixedOrPartialInputModes()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/Sample.cs");

        // Act
        Action missingInputMode = () => SourceCliValidation.ValidateCliArgs(null, null, null, null);
        Action mixedInputModes = () => SourceCliValidation.ValidateCliArgs(validFile, 10, 4, "Demo.Sample");
        Action partialLocationMode = () => SourceCliValidation.ValidateCliArgs(validFile, 10, null, null);

        // Assert
        Assert.Throws<ArgumentException>(missingInputMode);
        Assert.Throws<ArgumentException>(mixedInputModes);
        Assert.Throws<ArgumentException>(partialLocationMode);
    }

    [Fact]
    public void ValidateArgs_AllowsExactlyOneInputMode()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/Sample.cs");

        // Act
        var symbolMode = Record.Exception(() => SourceCliValidation.ValidateCliArgs(null, null, null, "Demo.Sample"));
        var locationMode = Record.Exception(() => SourceCliValidation.ValidateCliArgs(validFile, 10, 4, null));

        // Assert
        Assert.Null(symbolMode);
        Assert.Null(locationMode);
    }

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
