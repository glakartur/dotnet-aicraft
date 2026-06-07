using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputDescribeTests
{
    [Fact]
    public void SourceMethod_RendersSignatureLocationParamsModifiersDocAndSiblings()
    {
        var card = new DescribeCard(
            FullName: "Demo.Svc.Run",
            Kind: "method",
            File: "src/Svc.cs",
            Line: 5,
            Col: 17,
            ContainingType: "Demo.Svc",
            ContainingNamespace: "Demo",
            Signature: "public static int Run(string name, int count = 2)",
            ReturnType: "int",
            Parameters: new[]
            {
                new DescribeParameter("name", "string", null),
                new DescribeParameter("count", "int", "2")
            },
            Modifiers: new[] { "static" },
            Attributes: null,
            ConstantValue: null,
            Documentation: "Runs the thing.",
            Siblings: new[] { "public int Run(int n)" },
            Assembly: null);

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDescribe(card, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("describe:\n", output);
        Assert.Contains("public static int Run(string name, int count = 2)", output);
        Assert.Contains("  location: src/Svc.cs:5:17", output);
        Assert.Contains("  returns: int", output);
        Assert.Contains("    string name", output);
        Assert.Contains("    int count = 2", output);
        Assert.Contains("  modifiers: static", output);
        Assert.Contains("  doc:\n    Runs the thing.", output);
        Assert.Contains("  siblings:\n    public int Run(int n)", output);
    }

    [Fact]
    public void MetadataSymbol_RendersMetadataLocationWithAssembly()
    {
        var card = new DescribeCard(
            FullName: "System.String.Substring",
            Kind: "method",
            File: null, Line: null, Col: null,
            ContainingType: "System.String",
            ContainingNamespace: "System",
            Signature: "public string Substring(int startIndex)",
            ReturnType: "string",
            Parameters: new[] { new DescribeParameter("startIndex", "int", null) },
            Modifiers: null,
            Attributes: null,
            ConstantValue: null,
            Documentation: null,
            Siblings: null,
            Assembly: "System.Private.CoreLib");

        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteDescribe(card, "S.sln");
        var output = cap.GetOutput().Replace("\r\n", "\n");

        Assert.Contains("  location: <metadata> System.Private.CoreLib", output);
        Assert.DoesNotContain("siblings", output);
    }
}
