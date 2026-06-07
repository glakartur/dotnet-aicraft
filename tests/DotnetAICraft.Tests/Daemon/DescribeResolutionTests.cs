using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class DescribeResolutionTests
{
    [Fact]
    public async Task Describe_OverloadedMethod_EachCardListsOtherSiblingsExcludingTarget()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                public int Run(string s) => 0;
                public int Run(int n) => n;
            }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Run", null, null, null);

        Assert.Equal(2, groups.Count);
        foreach (var group in groups)
        {
            var card = Card(group);
            Assert.NotNull(card.Siblings);
            // The sibling list never repeats the target's own signature.
            Assert.DoesNotContain(card.Siblings!, s => s == card.Signature);
            Assert.Single(card.Siblings!); // exactly the one other overload
        }
    }

    [Fact]
    public async Task Describe_SourceMethod_HasSignatureParamsModifiersDocLocation()
    {
        var (solution, filePath) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                /// <summary>Runs the thing.</summary>
                public static int Run(string name, int count = 2) => count;
            }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Run", null, null, null);

        var card = Card(Assert.Single(groups));
        Assert.Contains("public static", card.Signature);
        Assert.Equal("int", card.ReturnType);
        Assert.NotNull(card.Parameters);
        Assert.Collection(card.Parameters!,
            p => { Assert.Equal("name", p.Name); Assert.Equal("string", p.Type); },
            p => { Assert.Equal("count", p.Name); Assert.Equal("2", p.DefaultValue); });
        Assert.Contains("static", card.Modifiers!);
        Assert.Equal("Runs the thing.", card.Documentation);
        Assert.NotNull(card.File);
        Assert.Equal(filePath, card.File);
    }

    [Fact]
    public async Task Describe_Type_HasBaseInterfacesTypeParamsModifiersAndNoMemberList()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public interface IThing {}
            public abstract class Base {}
            /// <summary>A widget.</summary>
            public sealed class Widget<T> : Base, IThing where T : struct {}
            """);

        // Generic types aren't addressable by bare --symbol FQN; resolve by the declaration location.
        // Line 5: "public sealed class Widget<T> ..." — "Widget" begins at column 21.
        var groups = await DaemonServer.ResolveDescribeAsync(solution, symbol: null, file: FilePath, line: 5, col: 22);

        var card = Card(Assert.Single(groups));
        Assert.Equal("class", card.Kind);
        Assert.Equal("public sealed class Widget<T> : Base, IThing where T : struct", card.Signature);
        Assert.Equal("A widget.", card.Documentation);
        Assert.Null(card.ReturnType);
        Assert.Null(card.Parameters);
    }

    [Fact]
    public async Task Describe_MetadataSymbol_HasNullCoordinatesAndAssembly()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                public string Greet(string name) => name.Substring(0);
            }
            """);

        // Point at "Substring" in name.Substring(0) → resolves the metadata System.String.Substring.
        var groups = await DaemonServer.ResolveDescribeAsync(
            solution, symbol: null, file: FilePath, line: 4, col: 47);

        var card = Card(Assert.Single(groups));
        Assert.Null(card.File);
        Assert.Null(card.Line);
        Assert.Null(card.Col);
        Assert.Contains("Substring", card.Signature);
        Assert.NotNull(card.Assembly);
    }

    [Fact]
    public async Task Describe_ConstField_IncludesConstantValue()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                public const int Max = 42;
            }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Max", null, null, null);

        var card = Card(Assert.Single(groups));
        Assert.Equal("42", card.ConstantValue);
        Assert.Contains("const", card.Modifiers!);
    }

    [Fact]
    public async Task Describe_EnumMember_IncludesConstantValue()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public enum Color { Red = 1, Green = 2 }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Color.Green", null, null, null);

        var card = Card(Assert.Single(groups));
        Assert.Equal("2", card.ConstantValue);
    }

    [Fact]
    public async Task Describe_GenericMethod_ShowsTypeParametersAndConstraints()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                public T Pick<T>(T a, T b) where T : class => a;
            }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Pick", null, null, null);

        var card = Card(Assert.Single(groups));
        Assert.Contains("Pick<T>", card.Signature);
        Assert.Contains("where T : class", card.Signature);
    }

    [Fact]
    public async Task Describe_MethodWithAttribute_IncludesStrippedAttributeName()
    {
        var (solution, _) = BuildSolution("""
            using System;
            namespace Demo;
            public class Svc
            {
                [Obsolete]
                public void Run() {}
            }
            """);

        var groups = await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Run", null, null, null);

        var card = Card(Assert.Single(groups));
        Assert.NotNull(card.Attributes);
        Assert.Contains("Obsolete", card.Attributes!); // "Attribute" suffix stripped
    }

    [Fact]
    public async Task Describe_BoolAndStringConstFields_FormatConstantValues()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo;
            public class Svc
            {
                public const bool Flag = true;
                public const string Label = "hi";
            }
            """);

        var flag = Card(Assert.Single(
            await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Flag", null, null, null)));
        var label = Card(Assert.Single(
            await DaemonServer.ResolveDescribeAsync(solution, "Demo.Svc.Label", null, null, null)));

        Assert.Equal("true", flag.ConstantValue);
        Assert.Equal("\"hi\"", label.ConstantValue);
    }

    [Fact]
    public async Task Describe_Namespace_ThrowsRedirect()
    {
        var (solution, _) = BuildSolution("""
            namespace Demo.Services;
            public class Svc {}
            """);

        var ex = await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveDescribeAsync(solution, "Demo.Services", null, null, null));

        Assert.Equal("INVALID_PARAMS", ex.Error.Code);
        Assert.Contains("namespace", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbols", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DescribeCard Card(SymbolMatchGroup group) => (DescribeCard)group.Result;

    private const string FilePath = "/virtual/Sample.cs";

    private static (Solution Solution, string FilePath) BuildSolution(string code)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(string).Assembly.Location));
        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId), "Sample.cs",
            Microsoft.CodeAnalysis.Text.SourceText.From(code),
            filePath: FilePath);
        return (solution, FilePath);
    }
}
