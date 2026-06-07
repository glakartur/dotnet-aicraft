using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotnetAICraft.Tests.Roslyn;

public class SymbolDisplayFormatsTests
{
    [Fact]
    public void MemberSignatureFormat_Method_RendersAccessibilityModifiersNrtDefaultAndParams()
    {
        var compilation = Compile("""
            using System.Threading.Tasks;
            namespace Demo;
            public class Svc
            {
                public static Task<int> Foo(string? s, int n = 3, params int[] rest) => Task.FromResult(0);
            }
            """);
        var method = MemberOf(compilation, "Demo.Svc", "Foo");

        var signature = SymbolDisplayFormats.FormatMemberSignature(method);

        Assert.Contains("public static", signature);
        Assert.Contains("Task<int>", signature);
        Assert.Contains("Foo(", signature);
        Assert.Contains("string?", signature);   // NRT annotation
        Assert.Contains("int n = 3", signature);  // default value
        Assert.Contains("params int[] rest", signature);
        Assert.DoesNotContain("System.Int32", signature); // UseSpecialTypes
    }

    [Fact]
    public void FormatMemberSignature_AsyncMethod_PrependsAsync()
    {
        var compilation = Compile("""
            using System.Threading.Tasks;
            namespace Demo;
            public class Svc
            {
                public async Task Work() => await Task.CompletedTask;
            }
            """);
        var method = MemberOf(compilation, "Demo.Svc", "Work");

        var signature = SymbolDisplayFormats.FormatMemberSignature(method);

        Assert.StartsWith("async ", signature);
    }

    [Fact]
    public void TypeHeaderFormat_GenericSealedClass_RendersAccessibilityModifiersBaseInterfacesConstraints()
    {
        var compilation = Compile("""
            namespace Demo;
            public class Bar {}
            public interface IBaz {}
            public sealed class Foo<T> : Bar, IBaz where T : struct {}
            """);
        var type = TypeOf(compilation, "Demo.Foo`1");

        var header = SymbolDisplayFormats.FormatTypeHeader(type);

        Assert.Equal("public sealed class Foo<T> : Bar, IBaz where T : struct", header);
    }

    [Fact]
    public void TypeHeaderFormat_PlainClass_OmitsSystemObjectBase()
    {
        var compilation = Compile("""
            namespace Demo;
            public class Plain {}
            """);
        var type = TypeOf(compilation, "Demo.Plain");

        var header = SymbolDisplayFormats.FormatTypeHeader(type);

        Assert.Equal("public class Plain", header);
        Assert.DoesNotContain(":", header);
    }

    [Fact]
    public void OutlineMemberFormat_Method_IsCompactWithoutDefaultValuesAndUsesSpecialTypes()
    {
        var compilation = Compile("""
            namespace Demo;
            public class Svc
            {
                public int Add(int a, int b = 2) => a + b;
            }
            """);
        var method = MemberOf(compilation, "Demo.Svc", "Add");

        var line = SymbolDisplayFormats.FormatMemberSignature(method, SymbolDisplayFormats.OutlineMemberFormat);

        Assert.Contains("public int Add(int a, int b)", line);
        Assert.DoesNotContain("= 2", line);
        Assert.DoesNotContain("System.Int32", line);
    }

    [Fact]
    public void OutlineMemberFormat_Property_RendersNameOnlyWithoutAccessorList()
    {
        var compilation = Compile("""
            namespace Demo;
            public class Svc
            {
                public string Name { get; set; } = "";
            }
            """);
        var property = MemberOf(compilation, "Demo.Svc", "Name");

        var line = SymbolDisplayFormats.FormatMemberSignature(property, SymbolDisplayFormats.OutlineMemberFormat);

        Assert.Contains("public string Name", line);
        Assert.DoesNotContain("{", line);
    }

    [Fact]
    public void FormatDelegateSignature_GenericDelegate_RendersAccessibilityReturnParamsAndConstraints()
    {
        var compilation = Compile("""
            namespace Demo;
            public delegate T Transformer<T>(T input, int count) where T : class;
            """);
        var del = TypeOf(compilation, "Demo.Transformer`1");

        var signature = SymbolDisplayFormats.FormatDelegateSignature(del);

        Assert.Equal("public delegate T Transformer<T>(T input, int count) where T : class", signature);
    }

    [Fact]
    public void FormatTypeHeader_StaticClass_EmitsStaticOnlyNotAbstractSealed()
    {
        var compilation = Compile("""
            namespace Demo;
            public static class Helpers {}
            """);
        var header = SymbolDisplayFormats.FormatTypeHeader(TypeOf(compilation, "Demo.Helpers"));

        Assert.Equal("public static class Helpers", header);
        Assert.DoesNotContain("abstract", header);
        Assert.DoesNotContain("sealed", header);
    }

    [Fact]
    public void FormatTypeHeader_AbstractClass_EmitsAbstract()
    {
        var compilation = Compile("""
            namespace Demo;
            public abstract class Shape {}
            """);
        Assert.Equal("public abstract class Shape",
            SymbolDisplayFormats.FormatTypeHeader(TypeOf(compilation, "Demo.Shape")));
    }

    [Fact]
    public void FormatTypeHeader_ReadonlyStruct_EmitsReadonly()
    {
        var compilation = Compile("""
            namespace Demo;
            public readonly struct Point {}
            """);
        Assert.Equal("public readonly struct Point",
            SymbolDisplayFormats.FormatTypeHeader(TypeOf(compilation, "Demo.Point")));
    }

    [Fact]
    public void FormatTypeHeader_Record_UsesRecordKeyword()
    {
        var compilation = Compile("""
            namespace Demo;
            public record Money(decimal Amount);
            """);
        var header = SymbolDisplayFormats.FormatTypeHeader(TypeOf(compilation, "Demo.Money"));

        Assert.StartsWith("public", header);
        Assert.Contains("record Money", header);
    }

    private static ISymbol MemberOf(Compilation compilation, string typeMetadataName, string memberName)
        => TypeOf(compilation, typeMetadataName).GetMembers(memberName).First();

    private static INamedTypeSymbol TypeOf(Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName)
           ?? throw new InvalidOperationException($"Type '{metadataName}' not found.");

    private static CSharpCompilation Compile(string code)
        => CSharpCompilation.Create(
            "DisplayFormatTests",
            new[] { CSharpSyntaxTree.ParseText(code) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
