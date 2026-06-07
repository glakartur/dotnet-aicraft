using DotnetAICraft.Roslyn;
using Xunit;

namespace DotnetAICraft.Tests.Roslyn;

public class XmlDocCleanerTests
{
    [Fact]
    public void Clean_SeeCref_RendersSimpleName()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>Delegates to <see cref="T:Demo.Other"/> for work.</summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("Delegates to Other for work.", cleaned);
    }

    [Fact]
    public void Clean_Paramref_RendersParameterName()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>Uses <paramref name="count"/> times.</summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("Uses count times.", cleaned);
    }

    [Fact]
    public void Clean_Para_ProducesBlankLineBreak()
    {
        var xml = """
            <member name="T:Demo.Svc">
                <summary>
                <para>First paragraph.</para>
                <para>Second paragraph.</para>
                </summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", cleaned);
    }

    [Fact]
    public void Clean_Code_PreservesInteriorWhitespace()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>
                Example:
                <code>
                var x = 1;
                    y = 2;
                </code>
                </summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Contains("var x = 1;\n    y = 2;", cleaned);
    }

    [Fact]
    public void Clean_TrimsPerLineDocIndentation()
    {
        // Mirrors GetDocumentationCommentXml output: text nodes carry the leading newline + indentation
        // that followed each `///`.
        var xml = "<member name=\"M:Demo.Svc.Run\">\n    <summary>\n    First line.\n    Second line.\n    </summary>\n</member>";

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("First line. Second line.", cleaned);
    }

    [Fact]
    public void Clean_InlineCodeElement_DoesNotInsertParagraphBreaks()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>Returns <c>null</c> when empty, or <c>true</c> otherwise.</summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("Returns null when empty, or true otherwise.", cleaned);
    }

    [Fact]
    public void Clean_Seealso_RendersSimpleName()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>See <seealso cref="T:Demo.Other"/>.</summary>
            </member>
            """;

        Assert.Equal("See Other.", XmlDocCleaner.Clean(xml));
    }

    [Fact]
    public void Clean_List_RendersBulletItems()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <summary>
                <list type="bullet">
                <item>First.</item>
                <item>Second.</item>
                </list>
                </summary>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Contains("- First.", cleaned);
        Assert.Contains("- Second.", cleaned);
    }

    [Fact]
    public void Clean_OnlyInheritdoc_ReturnsMarker()
    {
        var xml = """
            <member name="M:Demo.Svc.Run">
                <inheritdoc/>
            </member>
            """;

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.Equal("(inherited documentation)", cleaned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_EmptyInput_ReturnsNull(string? xml)
    {
        Assert.Null(XmlDocCleaner.Clean(xml));
    }

    [Fact]
    public void Clean_MalformedXml_FallsBackToTagStrippedText_NoThrow()
    {
        var xml = "<summary>Unclosed tag and <see cref=broken text";

        var cleaned = XmlDocCleaner.Clean(xml);

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("<", cleaned);
        Assert.Contains("Unclosed tag", cleaned);
    }
}
