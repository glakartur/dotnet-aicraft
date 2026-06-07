using System.CommandLine;
using DotnetAICraft.Commands;
using DotnetAICraft.Commands.Describe;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Commands;

public class DescribeCommandTests
{
    [Fact]
    public void Build_ExposesExpectedOptionsAndAliases()
    {
        var command = DescribeCommand.Build(
            new Option<FileInfo>("--solution", "-s") { Required = false },
            new Option<FileInfo>("--project", "-p") { Required = false },
            new Option<string?>("--idle-timeout"),
            formatOption: new Option<OutputFormat>("--format") { DefaultValueFactory = _ => OutputFormat.Text });

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
        Assert.Throws<ArgumentException>(() => Validation.ValidateCliArgs(null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            Validation.ValidateCliArgs(new FileInfo("/tmp/Sample.cs"), 10, 4, "Demo.Sample"));
        Assert.Throws<ArgumentException>(() =>
            Validation.ValidateCliArgs(new FileInfo("/tmp/Sample.cs"), 10, null, null));
    }

    [Fact]
    public void ValidateArgs_AllowsExactlyOneInputMode()
    {
        Validation.ValidateCliArgs(null, null, null, "Demo.Sample");
        Validation.ValidateCliArgs(new FileInfo("/tmp/Sample.cs"), 10, 4, null);
    }

    private static void AssertContainsOption(Command command, string alias)
        => Assert.Contains(command.Options, opt =>
            string.Equals(opt.Name, alias, StringComparison.Ordinal) ||
            opt.Aliases.Contains(alias));
}
