using DotnetAICraft.Models;
using DotnetAICraft.Output;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Output;

[Collection("Console output")]
public class TextOutputListTests
{
    [Fact]
    public void Refs_Happy_RendersLabelAndRows()
    {
        var items = new[]
        {
            new ReferenceResult("/a/File1.cs", 10, 4, "var x = 1;"),
            new ReferenceResult("/a/File2.cs", 20, 8, "y = x;"),
            new ReferenceResult("/a/File3.cs", 30, 1, "x;")
        };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(items, "Demo.Sample", "MySolution.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("references:", lines[0]);
        Assert.Equal("/a/File1.cs:10:4: var x = 1;", lines[1]);
        Assert.Equal("/a/File2.cs:20:8: y = x;", lines[2]);
        Assert.Equal("/a/File3.cs:30:1: x;", lines[3]);
    }

    [Fact]
    public void Refs_NoTargetOrSolutionInOutput()
    {
        var items = new[] { new ReferenceResult("/a/F.cs", 1, 1, "x") };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(items, "Demo.Sample", "MySolution.sln");
        var text = cap.GetOutput();
        Assert.DoesNotContain("Demo.Sample", text);
        Assert.DoesNotContain("MySolution.sln", text);
    }

    [Fact]
    public void Refs_Empty_LabelWithNoResultsOnly()
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(Array.Empty<ReferenceResult>(), "T", "S.sln");
        Assert.Equal($"references: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void Refs_PathWithDriveColon_PreservedAsIs()
    {
        var items = new[] { new ReferenceResult("C:/path/Foo.cs", 42, 17, "body") };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(items, "T", "S.sln");
        Assert.Contains("C:/path/Foo.cs:42:17: body", cap.GetOutput());
    }

    [Fact]
    public void Refs_BodyNewlines_RenderedOnOneLine()
    {
        var items = new[] { new ReferenceResult("/a/F.cs", 1, 1, "line1\nline2") };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(items, "T", "S.sln");
        Assert.Contains("/a/F.cs:1:1: line1 line2", cap.GetOutput());
    }

    [Fact]
    public void Impls_Happy_UsesImplementationsLabel()
    {
        var items = new[] { new SymbolResult("Foo", "Ns.Foo", "Class", "/a/F.cs", 1, 1, null, "Ns") };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteImpls(items, "IFoo", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("implementations:", lines[0]);
        Assert.Equal("/a/F.cs:1:1: Class Ns.Foo", lines[1]);
    }

    [Fact]
    public void Impls_Empty_LabelWithNoResults()
    {
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteImpls(Array.Empty<SymbolResult>(), "IFoo", "S.sln");
        Assert.Equal($"implementations: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void Callers_Incoming_RendersFlatList_NoRootRow()
    {
        var nodes = new List<CallGraphNode>
        {
            new("root", "Demo.Target", "method", "/a/T.cs", 5, 5, null, null),
            new("a", "Demo.CallerA", "method", "/a/A.cs", 10, 4, null, null),
            new("b", "Demo.CallerB", "method", "/a/B.cs", 20, 4, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("a", "root", "incoming", true),
            new("b", "root", "incoming", false)
        };
        var graph = new CallGraphResult("root", "incoming", 1, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "Demo.Target", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("callers:", lines[0]);
        Assert.Equal("/a/A.cs:10:4: method Demo.CallerA", lines[1]);
        Assert.Equal("/a/B.cs:20:4: method Demo.CallerB", lines[2]);
        Assert.DoesNotContain("Demo.Target", cap.GetOutput());
    }

    [Fact]
    public void Callers_DepthN_Branching_IndentsByTwoSpacesPerLevel()
    {
        // root <- A <- B <- C ; root <- D (incoming chain)
        var nodes = new List<CallGraphNode>
        {
            new("root", "T", "method", "/a/T.cs", 1, 1, null, null),
            new("a", "A", "method", "/a/A.cs", 2, 1, null, null),
            new("b", "B", "method", "/a/B.cs", 3, 1, null, null),
            new("c", "C", "method", "/a/C.cs", 4, 1, null, null),
            new("d", "D", "method", "/a/D.cs", 5, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("a", "root", "incoming", true),
            new("b", "a", "incoming", true),
            new("c", "b", "incoming", true),
            new("d", "root", "incoming", true)
        };
        var graph = new CallGraphResult("root", "incoming", 3, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "T", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("callers:", lines[0]);
        Assert.Equal("/a/A.cs:2:1: method A", lines[1]);
        Assert.Equal("  /a/B.cs:3:1: method B", lines[2]);
        Assert.Equal("    /a/C.cs:4:1: method C", lines[3]);
        Assert.Equal("/a/D.cs:5:1: method D", lines[4]);
    }

    [Fact]
    public void Callers_SymbolReachableThroughTwoPaths_PrintedUnderEachParent()
    {
        // root <- A <- X ; root <- B <- X
        var nodes = new List<CallGraphNode>
        {
            new("root", "T", "method", "/a/T.cs", 1, 1, null, null),
            new("a", "A", "method", "/a/A.cs", 2, 1, null, null),
            new("b", "B", "method", "/a/B.cs", 3, 1, null, null),
            new("x", "X", "method", "/a/X.cs", 9, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("a", "root", "incoming", true),
            new("b", "root", "incoming", true),
            new("x", "a", "incoming", true),
            new("x", "b", "incoming", true)
        };
        var graph = new CallGraphResult("root", "incoming", 2, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "T", "S.sln");
        var text = cap.GetOutput();
        var occurrences = text.Split("/a/X.cs:9:1: method X").Length - 1;
        Assert.Equal(2, occurrences);
        Assert.Contains("  /a/X.cs:9:1: method X", text);
        Assert.StartsWith("callers:", text);
    }

    [Fact]
    public void Callers_Cycle_MarkedAndNotRecursed()
    {
        // root <- A <- root (cycle)
        var nodes = new List<CallGraphNode>
        {
            new("root", "T", "method", "/a/T.cs", 1, 1, null, null),
            new("a", "A", "method", "/a/A.cs", 2, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("a", "root", "incoming", true),
            new("root", "a", "incoming", true)
        };
        var graph = new CallGraphResult("root", "incoming", 3, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "T", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("callers:", lines[0]);
        Assert.Equal("/a/A.cs:2:1: method A", lines[1]);
        Assert.Equal("  /a/T.cs:1:1: method T (cycle)", lines[2]);
    }

    [Fact]
    public void Callees_OutgoingDirection_UsesCalleesLabelAndDropsRoot()
    {
        // root -> c1 -> c2
        var nodes = new List<CallGraphNode>
        {
            new("root", "R", "method", "/a/R.cs", 1, 1, null, null),
            new("c1", "C1", "method", "/a/C1.cs", 2, 1, null, null),
            new("c2", "C2", "method", "/a/C2.cs", 3, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("root", "c1", "outgoing", true),
            new("c1", "c2", "outgoing", true)
        };
        var graph = new CallGraphResult("root", "outgoing", 2, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "R", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("callees:", lines[0]);
        Assert.Equal("/a/C1.cs:2:1: method C1", lines[1]);
        Assert.Equal("  /a/C2.cs:3:1: method C2", lines[2]);
        Assert.DoesNotContain("method R", cap.GetOutput());
    }

    [Fact]
    public void Callees_Empty_LabelWithNoResults()
    {
        var nodes = new List<CallGraphNode>
        {
            new("root", "R", "method", "/a/R.cs", 1, 1, null, null)
        };
        var graph = new CallGraphResult("root", "outgoing", 1, nodes, Array.Empty<CallGraphEdge>());
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "R", "S.sln");
        Assert.Equal($"callees: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void CallGraph_BothDirection_EmitsTwoSections()
    {
        var nodes = new List<CallGraphNode>
        {
            new("root", "R", "method", "/a/R.cs", 1, 1, null, null),
            new("caller", "Caller", "method", "/a/Caller.cs", 2, 1, null, null),
            new("callee", "Callee", "method", "/a/Callee.cs", 3, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("caller", "root", "incoming", true),
            new("root", "callee", "outgoing", true)
        };
        var graph = new CallGraphResult("root", "both", 1, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "R", "S.sln");
        var text = cap.GetOutput();
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("callers:", lines[0]);
        Assert.Equal("/a/Caller.cs:2:1: method Caller", lines[1]);
        Assert.Equal("callees:", lines[2]);
        Assert.Equal("/a/Callee.cs:3:1: method Callee", lines[3]);
        Assert.DoesNotContain("method R", text);
    }

    [Fact]
    public void CallGraph_BothDirection_OneEmptyBranch_EmitsBothLabels()
    {
        var nodes = new List<CallGraphNode>
        {
            new("root", "R", "method", "/a/R.cs", 1, 1, null, null),
            new("caller", "Caller", "method", "/a/Caller.cs", 2, 1, null, null)
        };
        var edges = new List<CallGraphEdge>
        {
            new("caller", "root", "incoming", true)
        };
        var graph = new CallGraphResult("root", "both", 1, nodes, edges);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "R", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("callers:", lines[0]);
        Assert.Equal("/a/Caller.cs:2:1: method Caller", lines[1]);
        Assert.Equal("callees: (no results)", lines[2]);
    }

    [Fact]
    public void Callers_Empty_LabelWithNoResultsOnly()
    {
        var nodes = new List<CallGraphNode>
        {
            new("root", "T", "method", "/a/T.cs", 1, 1, null, null)
        };
        var graph = new CallGraphResult("root", "incoming", 1, nodes, Array.Empty<CallGraphEdge>());
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "T", "S.sln");
        Assert.Equal($"callers: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void Callers_RootMissing_RendersNoResults()
    {
        var graph = new CallGraphResult(
            "missing-root",
            "incoming",
            1,
            Array.Empty<CallGraphNode>(),
            Array.Empty<CallGraphEdge>());
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteCallers(graph, "T", "S.sln");
        Assert.Equal($"callers: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void Symbols_Paging_HintInLabelAnnotation()
    {
        var page = new SymbolsResultPage(new[]
        {
            new SymbolResult("Foo", "N.Foo", "class", "/a/Foo.cs", 1, 1, null, "N")
        }, HasMore: true);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSymbols(page, "Foo*", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("symbols: (more available — use --offset to continue)", lines[0]);
        Assert.Equal("/a/Foo.cs:1:1: class N.Foo", lines[1]);
    }

    [Fact]
    public void Symbols_NoPaging_PlainLabel()
    {
        var page = new SymbolsResultPage(new[]
        {
            new SymbolResult("Foo", "N.Foo", "class", "/a/Foo.cs", 1, 1, null, "N"),
            new SymbolResult("Bar", "N.Bar", "class", "/a/Bar.cs", 2, 1, null, "N")
        }, HasMore: false);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSymbols(page, "*", "S.sln");
        var text = cap.GetOutput();
        Assert.StartsWith("symbols:" + Environment.NewLine, text);
        Assert.DoesNotContain("more available", text);
    }

    [Fact]
    public void Symbols_Empty_LabelWithNoResults()
    {
        var page = new SymbolsResultPage(Array.Empty<SymbolResult>(), HasMore: false);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteSymbols(page, "*", "S.sln");
        Assert.Equal($"symbols: (no results){Environment.NewLine}", cap.GetOutput());
    }

    [Fact]
    public void Unused_Happy_LabelCarriesFilterAnnotation()
    {
        var items = new[]
        {
            new UnusedCandidateResult("N.Foo", "class", "/a/F.cs", 1, 1, "Proj", "no references", 0.95)
        };
        var summary = new UnusedScanSummary("class", "Proj", PublicOnly: true, IncludeGenerated: false, Scanned: 42, Items: items);
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteUnused(summary, "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("unused: (scanned 42, publicOnly=true, includeGenerated=false)", lines[0]);
        Assert.Equal("/a/F.cs:1:1: class N.Foo [confidence=0.95] (no references)", lines[1]);
    }

    [Fact]
    public void Unused_Empty_AnnotationStillCarriesFilters()
    {
        var summary = new UnusedScanSummary(
            "class", "Proj", PublicOnly: true, IncludeGenerated: false, Scanned: 5,
            Items: Array.Empty<UnusedCandidateResult>());
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteUnused(summary, "S.sln");
        Assert.Equal(
            $"unused: (no results, scanned 5, publicOnly=true, includeGenerated=false){Environment.NewLine}",
            cap.GetOutput());
    }

    [Fact]
    public void NoColumnPadding_AcrossRows()
    {
        var items = new[]
        {
            new ReferenceResult("/short.cs", 1, 1, "x"),
            new ReferenceResult("/much/longer/path/file.cs", 200, 30, "y")
        };
        using var cap = ConsoleOutputCapture.Start();
        TextOutput.WriteRefs(items, "T", "S.sln");
        var lines = cap.GetOutput().Split(Environment.NewLine);
        Assert.Equal("references:", lines[0]);
        Assert.Equal("/short.cs:1:1: x", lines[1]);
        Assert.Equal("/much/longer/path/file.cs:200:30: y", lines[2]);
    }
}
