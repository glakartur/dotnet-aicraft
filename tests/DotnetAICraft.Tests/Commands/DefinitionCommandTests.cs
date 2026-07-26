using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Definition;
using DotnetAICraft.Output;
using Xunit;
using DefinitionCliValidation = DotnetAICraft.Commands.Definition.CliValidation;

namespace DotnetAICraft.Tests.Commands;

public class DefinitionCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptionsAndAliases()
    {
        // Arrange
        var solutionOption = BuildSolutionOption();
        var idleTimeoutOption = BuildIdleTimeoutOption();
        var projectOption = BuildProjectOption();
        var formatOption = BuildFormatOption();

        // Act
        var command = DefinitionCommand.Build(solutionOption, projectOption, idleTimeoutOption, formatOption: formatOption);

        // Assert
        Assert.Equal("definition", command.Name);
        AssertContainsOption(command, "--solution");
        AssertContainsOption(command, "-s");
        AssertContainsOption(command, "--file");
        AssertContainsOption(command, "--line");
        AssertContainsOption(command, "--col");
        AssertContainsOption(command, "--symbol");
        AssertContainsOption(command, "--idle-timeout");
        AssertContainsOption(command, "--format");
    }

    private static Option<OutputFormat> BuildFormatOption()
        => new("--format") { DefaultValueFactory = _ => OutputFormat.Text };

    [Fact]
    public void ValidateArgs_RejectsMissingMixedOrPartialInputModes()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/Sample.cs");

        // Act
        Action missingInputMode = () => DefinitionCliValidation.ValidateCliArgs(file: null, line: null, col: null, symbol: null);
        Action mixedInputModes = () => DefinitionCliValidation.ValidateCliArgs(validFile, line: 10, col: 4, symbol: "Demo.Sample");
        Action partialLocationMode = () => DefinitionCliValidation.ValidateCliArgs(validFile, line: 10, col: null, symbol: null);

        // Assert
        AssertValidationFails(missingInputMode);
        AssertValidationFails(mixedInputModes);
        AssertValidationFails(partialLocationMode);
    }

    [Fact]
    public void ValidateArgs_AllowsExactlyOneInputMode()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/Sample.cs");

        // Act
        var symbolMode = Record.Exception(() => DefinitionCliValidation.ValidateCliArgs(file: null, line: null, col: null, symbol: "Demo.Sample"));
        var locationMode = Record.Exception(() => DefinitionCliValidation.ValidateCliArgs(validFile, line: 10, col: 4, symbol: null));

        // Assert
        AssertValidationSucceeds(symbolMode);
        AssertValidationSucceeds(locationMode);
    }

    private static Option<FileInfo> BuildSolutionOption()
        => new("--solution", "-s") { Required = false };

    private static Option<FileInfo> BuildProjectOption()
        => new("--project", "-p") { Required = false };

    private static Option<string?> BuildIdleTimeoutOption()
        => new("--idle-timeout");

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));

    private static void AssertValidationFails(Action act)
    {
        Assert.Throws<ArgumentException>(act);
    }

    private static void AssertValidationSucceeds(Exception? exception)
    {
        Assert.Null(exception);
    }
}
