using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class OutlineResolutionTests
{
    [Fact]
    public async Task Outline_Symbol_ListsDeclaredMembersIncludingPrivateInSourceOrderWithNesting()
    {
        var solution = BuildSolution(("/virtual/Outer.cs", """
            namespace Demo;
            public class Outer
            {
                public Outer(string name) {}
                private readonly string _name = "";
                public string Render() => _name;
                public class Inner
                {
                    public void N() {}
                }
            }
            """));

        var result = Result(Single(await DaemonServer.ResolveOutlineAsync(
            solution, "Demo.Outer", null, publicOnly: false, includeInherited: false)));

        var signatures = result.Declared.Select(m => m.Signature).ToList();
        Assert.Contains(signatures, s => s.Contains("Outer(string name)"));
        Assert.Contains(signatures, s => s.Contains("private") && s.Contains("_name")); // private kept by default
        Assert.Contains(signatures, s => s.Contains("Render()"));
        Assert.Contains(signatures, s => s.Contains("class") && s.Contains("Inner"));
        // Nested member N carries its declaring type so it's unambiguous.
        var nestedN = Assert.Single(result.Declared, m => m.Signature.Contains("N()"));
        Assert.Contains("Inner", nestedN.DeclaringType);
    }

    [Fact]
    public async Task Outline_File_ListsMembersForAllTopLevelTypes()
    {
        var solution = BuildSolution(("/virtual/Types.cs", """
            namespace Demo;
            public class A { public void Ma() {} }
            public class B { public void Mb() {} }
            """));

        var groups = await DaemonServer.ResolveOutlineAsync(
            solution, null, "/virtual/Types.cs", publicOnly: false, includeInherited: false);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => Result(g).Declared.Any(m => m.Signature.Contains("Ma()")));
        Assert.Contains(groups, g => Result(g).Declared.Any(m => m.Signature.Contains("Mb()")));
    }

    [Fact]
    public async Task Outline_PublicOnly_DropsPrivateAndPrivateProtectedKeepsRest()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public class Svc
            {
                public int Alpha;
                internal int Bravo;
                protected int Charlie;
                protected internal int Delta;
                private int Echo;
                private protected int Foxtrot;
            }
            """));

        var result = Result(Single(await DaemonServer.ResolveOutlineAsync(
            solution, "Demo.Svc", null, publicOnly: true, includeInherited: false)));

        var names = result.Declared.Select(m => m.Signature).ToList();
        Assert.Contains(names, s => s.Contains("Alpha"));    // public
        Assert.Contains(names, s => s.Contains("Bravo"));    // internal
        Assert.Contains(names, s => s.Contains("Charlie"));  // protected
        Assert.Contains(names, s => s.Contains("Delta"));    // protected internal
        Assert.DoesNotContain(names, s => s.Contains("Echo"));      // private
        Assert.DoesNotContain(names, s => s.Contains("Foxtrot"));   // private protected
        Assert.True(result.PublicOnly);
    }

    [Fact]
    public async Task Outline_IncludeInherited_MetadataBase_GroupsObjectMembersUnderAssemblyHeader()
    {
        var solution = BuildSolution(("/virtual/Widget.cs", """
            namespace Demo;
            public class Widget { public void Render() {} }
            """));

        var result = Result(Single(await DaemonServer.ResolveOutlineAsync(
            solution, "Demo.Widget", null, publicOnly: false, includeInherited: true)));

        Assert.Contains(result.Declared, m => m.Signature.Contains("Render()"));
        var objectGroup = Assert.Single(result.Inherited, g => g.DeclaringType == "object");
        Assert.NotNull(objectGroup.Assembly); // metadata base names its assembly
        Assert.Contains(objectGroup.Members, m => m.Signature.Contains("ToString()"));
    }

    [Fact]
    public async Task Outline_IncludeInherited_SuppressesOverridesAndTagsNewShadowed()
    {
        var solution = BuildSolution(("/virtual/Hier.cs", """
            namespace Demo;
            public class Base
            {
                public virtual void Foo() {}
                public void Bar() {}
            }
            public class Derived : Base
            {
                public override void Foo() {}
                public new void Bar() {}
            }
            """));

        var result = Result(Single(await DaemonServer.ResolveOutlineAsync(
            solution, "Demo.Derived", null, publicOnly: false, includeInherited: true)));

        var baseGroup = Assert.Single(result.Inherited, g => g.DeclaringType == "Demo.Base");
        // Foo was overridden → not repeated in the Base group (shown as the declared override).
        Assert.DoesNotContain(baseGroup.Members, m => m.Signature.Contains("Foo()"));
        // Bar was shadowed with `new` → present and tagged.
        var bar = Assert.Single(baseGroup.Members, m => m.Signature.Contains("Bar()"));
        Assert.Equal("hidden by new", bar.Tag);

        // System.Object lands after Base in the group ordering.
        Assert.True(result.Inherited.Count >= 2);
        Assert.Equal("object", result.Inherited[^1].DeclaringType);
    }

    [Fact]
    public async Task Outline_Symbol_Method_RedirectsToDescribe()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo;
            public class Svc { public void Run() {} }
            """));

        var ex = await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveOutlineAsync(solution, "Demo.Svc.Run", null, false, false));

        Assert.Equal("INVALID_PARAMS", ex.Error.Code);
        Assert.Contains("describe", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outline_Symbol_Namespace_RedirectsToSymbols()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", """
            namespace Demo.Services;
            public class Svc {}
            """));

        var ex = await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveOutlineAsync(solution, "Demo.Services", null, false, false));

        Assert.Equal("INVALID_PARAMS", ex.Error.Code);
        Assert.Contains("namespace", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbols", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outline_File_ZeroDeclarations_ReturnsEmptySuccess()
    {
        var solution = BuildSolution(("/virtual/Empty.cs", "using System;\n"));

        var groups = await DaemonServer.ResolveOutlineAsync(
            solution, null, "/virtual/Empty.cs", false, false);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task Outline_ValidationRejectsLineColAndBothModes()
    {
        var solution = BuildSolution(("/virtual/Svc.cs", "namespace Demo; public class Svc {}"));

        await Assert.ThrowsAsync<DaemonValidationException>(
            () => DaemonServer.ResolveOutlineAsync(solution, "Demo.Svc", "/virtual/Svc.cs", false, false));
    }

    private static SymbolMatchGroup<OutlineResult> Single(IReadOnlyList<SymbolMatchGroup<OutlineResult>> groups) => Assert.Single(groups);
    private static OutlineResult Result(SymbolMatchGroup<OutlineResult> group) => group.Result;

    private static Solution BuildSolution(params (string Path, string Code)[] files)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        solution = solution.AddMetadataReference(projectId,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        foreach (var (path, code) in files)
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(path),
                Microsoft.CodeAnalysis.Text.SourceText.From(code),
                filePath: path);
        }

        return solution;
    }
}
