using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Outline;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

public class OutlineCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptions()
    {
        var command = OutlineCommand.Build(
            new Option<FileInfo>("--solution", "-s") { Required = false },
            new Option<FileInfo>("--project", "-p") { Required = false },
            new Option<string?>("--idle-timeout"),
            formatOption: new Option<OutputFormat>("--format") { DefaultValueFactory = _ => OutputFormat.Text });

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
        Assert.Throws<ArgumentException>(() =>
            Validation.ValidateCliArgs(new FileInfo("/tmp/x.cs"), 3, null, null));
    }

    [Fact]
    public void ValidateArgs_RejectsBothSymbolAndFile()
    {
        Assert.Throws<ArgumentException>(() =>
            Validation.ValidateCliArgs(new FileInfo("/tmp/x.cs"), null, null, "Demo.Svc"));
    }

    [Fact]
    public void ValidateArgs_RejectsNeither()
    {
        Assert.Throws<ArgumentException>(() => Validation.ValidateCliArgs(null, null, null, null));
    }

    [Fact]
    public void ValidateArgs_AllowsExactlyOneOfSymbolOrFile()
    {
        Validation.ValidateCliArgs(null, null, null, "Demo.Svc");
        Validation.ValidateCliArgs(new FileInfo("/tmp/x.cs"), null, null, null);
    }

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
