using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Describe;
using DotnetAICraft.Output;
using Xunit;
using DescribeCliValidation = DotnetAICraft.Commands.Describe.CliValidation;

namespace DotnetAICraft.Tests.Commands;

public class DescribeCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptionsAndAliases()
    {
        // Arrange
        var solutionOption = new Option<FileInfo>("--solution", "-s") { Required = false };
        var projectOption = new Option<FileInfo>("--project", "-p") { Required = false };
        var idleTimeoutOption = new Option<string?>("--idle-timeout");
        var formatOption = new Option<OutputFormat>("--format") { DefaultValueFactory = _ => OutputFormat.Text };

        // Act
        var command = DescribeCommand.Build(
            solutionOption,
            projectOption,
            idleTimeoutOption,
            formatOption: formatOption);

        // Assert
        Assert.Equal("describe", command.Name);
        AssertContainsOption(command, "--solution");
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
        Action missingInputMode = () => DescribeCliValidation.ValidateCliArgs(null, null, null, null);
        Action mixedInputModes = () => DescribeCliValidation.ValidateCliArgs(validFile, 10, 4, "Demo.Sample");
        Action partialLocationMode = () => DescribeCliValidation.ValidateCliArgs(validFile, 10, null, null);

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
        var symbolMode = Record.Exception(() => DescribeCliValidation.ValidateCliArgs(null, null, null, "Demo.Sample"));
        var locationMode = Record.Exception(() => DescribeCliValidation.ValidateCliArgs(validFile, 10, 4, null));

        // Assert
        Assert.Null(symbolMode);
        Assert.Null(locationMode);
    }

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
