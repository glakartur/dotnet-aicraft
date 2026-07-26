using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Outline;
using DotnetAICraft.Output;
using Xunit;
using OutlineCliValidation = DotnetAICraft.Commands.Outline.CliValidation;

namespace DotnetAICraft.Tests.Commands;

public class OutlineCommandTests
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
        var command = OutlineCommand.Build(
            solutionOption,
            projectOption,
            idleTimeoutOption,
            formatOption: formatOption);

        // Assert
        Assert.Equal("outline", command.Name);
        AssertContainsOption(command, "--file");
        AssertContainsOption(command, "--symbol");
        AssertContainsOption(command, "--public-only");
        AssertContainsOption(command, "--include-inherited");
        AssertContainsOption(command, "--format");
    }

    [Fact]
    public void ValidateArgs_RejectsLineCol()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/x.cs");

        // Act
        Action act = () => OutlineCliValidation.ValidateCliArgs(validFile, 3, null, null);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ValidateArgs_RejectsBothSymbolAndFile()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/x.cs");

        // Act
        Action act = () => OutlineCliValidation.ValidateCliArgs(validFile, null, null, "Demo.Svc");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ValidateArgs_RejectsNeither()
    {
        // Arrange
        FileInfo? file = null;
        string? symbol = null;

        // Act
        Action act = () => OutlineCliValidation.ValidateCliArgs(file, null, null, symbol);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ValidateArgs_AllowsExactlyOneOfSymbolOrFile()
    {
        // Arrange
        var validFile = new FileInfo("/tmp/x.cs");

        // Act
        var symbolMode = Record.Exception(() => OutlineCliValidation.ValidateCliArgs(null, null, null, "Demo.Svc"));
        var fileMode = Record.Exception(() => OutlineCliValidation.ValidateCliArgs(validFile, null, null, null));

        // Assert
        Assert.Null(symbolMode);
        Assert.Null(fileMode);
    }

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
