using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputDefinitionTests
{
    [Fact]
    public void Happy_RendersDefinitionLabelAndSingleRow()
    {
        var def = new DefinitionResult("Demo.Service.DoWork", "method", "/a/Service.cs", 12, 5, "Service", "Demo");
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDefinition(def, "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("definition:", lines[0]);
        Assert.Equal("/a/Service.cs:12:5: method Demo.Service.DoWork", lines[1]);
    }

    [Fact]
    public void MissingLocation_RendersKindAndFullNameOnly()
    {
        var def = new DefinitionResult("System.String", "class", null, null, null, null, "System");
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDefinition(def, "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("definition:", lines[0]);
        Assert.Equal("class System.String", lines[1]);
    }
}
