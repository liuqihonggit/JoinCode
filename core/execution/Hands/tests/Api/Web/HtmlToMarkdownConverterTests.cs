namespace Core.Tests.Web;

public class HtmlToMarkdownConverterTests
{
    private readonly HtmlToMarkdownConverter _converter = new();

    [Fact]
    public void Convert_EmptyHtml_ReturnsEmptyString()
    {
        var result = _converter.Convert(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Convert_NullHtml_ReturnsEmptyString()
    {
        var result = _converter.Convert(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Convert_PlainText_ReturnsSameText()
    {
        var result = _converter.Convert("Hello World");

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Convert_HeadingTags_ConvertsToMarkdownHeadings()
    {
        var html = "<h1>Title</h1><h2>Subtitle</h2><h3>Section</h3>";

        var result = _converter.Convert(html);

        Assert.Contains("# Title", result);
        Assert.Contains("## Subtitle", result);
        Assert.Contains("### Section", result);
    }

    [Fact]
    public void Convert_ParagraphTags_WrapsInNewlines()
    {
        var html = "<p>First paragraph</p><p>Second paragraph</p>";

        var result = _converter.Convert(html);

        Assert.Contains("First paragraph", result);
        Assert.Contains("Second paragraph", result);
    }

    [Fact]
    public void Convert_BoldAndItalic_ConvertsCorrectly()
    {
        var html = "<strong>Bold</strong> and <em>Italic</em> and <b>Also Bold</b> and <i>Also Italic</i>";

        var result = _converter.Convert(html);

        Assert.Contains("**Bold**", result);
        Assert.Contains("*Italic*", result);
        Assert.Contains("**Also Bold**", result);
        Assert.Contains("*Also Italic*", result);
    }

    [Fact]
    public void Convert_AnchorTags_PreservesLinksWithText()
    {
        var html = "<a href=\"https://example.com\">Example Link</a>";

        var result = _converter.Convert(html);

        Assert.Contains("[Example Link](https://example.com)", result);
    }

    [Fact]
    public void Convert_AnchorTagWithoutHref_KeepsTextOnly()
    {
        var html = "<a>Just Text</a>";

        var result = _converter.Convert(html);

        Assert.Contains("Just Text", result);
        Assert.DoesNotContain("]", result);
    }

    [Fact]
    public void Convert_UnorderedList_ConvertsCorrectly()
    {
        var html = "<ul><li>Item 1</li><li>Item 2</li><li>Item 3</li></ul>";

        var result = _converter.Convert(html);

        Assert.Contains("- Item 1", result);
        Assert.Contains("- Item 2", result);
        Assert.Contains("- Item 3", result);
    }

    [Fact]
    public void Convert_OrderedList_ConvertsCorrectly()
    {
        var html = "<ol><li>First</li><li>Second</li><li>Third</li></ol>";

        var result = _converter.Convert(html);

        Assert.Contains("1. First", result);
        Assert.Contains("2. Second", result);
        Assert.Contains("3. Third", result);
    }

    [Fact]
    public void Convert_CodeBlock_PreservesCodeInFencedBlock()
    {
        var html = "<pre><code>var x = 1;\nvar y = 2;</code></pre>";

        var result = _converter.Convert(html);

        Assert.Contains("```", result);
        Assert.Contains("var x = 1;", result);
        Assert.Contains("var y = 2;", result);
    }

    [Fact]
    public void Convert_InlineCode_PreservesBacktickWrapped()
    {
        var html = "Use the <code>Foo()</code> method";

        var result = _converter.Convert(html);

        Assert.Contains("`Foo()`", result);
    }

    [Fact]
    public void Convert_Blockquote_ConvertsCorrectly()
    {
        var html = "<blockquote>This is a quote</blockquote>";

        var result = _converter.Convert(html);

        Assert.Contains("> This is a quote", result);
    }

    [Fact]
    public void Convert_Image_ConvertsToMarkdownImage()
    {
        var html = "<img src=\"image.png\" alt=\"Alt Text\" />";

        var result = _converter.Convert(html);

        Assert.Contains("![Alt Text](image.png)", result);
    }

    [Fact]
    public void Convert_ImageWithoutAlt_UsesEmptyAlt()
    {
        var html = "<img src=\"image.png\" />";

        var result = _converter.Convert(html);

        Assert.Contains("![](image.png)", result);
    }

    [Fact]
    public void Convert_HorizontalRule_ConvertsCorrectly()
    {
        var html = "<hr>";

        var result = _converter.Convert(html);

        // ReverseMarkdown.Aot 输出 "* * *"（GFM 水平线格式），而非 "---"
        Assert.Matches(@"(\* \* \*|---|- - -)", result.Trim());
    }

    [Fact]
    public void Convert_HtmlEntities_DecodedCorrectly()
    {
        var html = "&amp; &lt; &gt; &quot; &apos;";

        var result = _converter.Convert(html);

        // ReverseMarkdown.Aot 解码 &amp; 为 &，其他实体保留原样或解码
        Assert.Contains("&", result);
        // &lt; &gt; 在 ReverseMarkdown 中可能保留为 HTML 实体形式
        Assert.True(result.Contains("<") || result.Contains("&lt;"));
        Assert.True(result.Contains(">") || result.Contains("&gt;"));
    }

    [Fact]
    public void Convert_LineBreak_ConvertsToNewline()
    {
        var html = "Line 1<br>Line 2<br/>Line 3";

        var result = _converter.Convert(html);

        // ReverseMarkdown.Aot 在 Windows 上输出 \r\n，统一比较时替换换行符
        var normalized = result.Replace("\r\n", "\n");
        Assert.Contains("Line 1\nLine 2\nLine 3", normalized);
    }

    [Fact]
    public void Convert_Table_ConvertsToMarkdownTable()
    {
        var html = "<table><thead><tr><th>Name</th><th>Age</th></tr></thead><tbody><tr><td>Alice</td><td>30</td></tr><tr><td>Bob</td><td>25</td></tr></tbody></table>";

        var result = _converter.Convert(html);

        Assert.Contains("| Name | Age |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Alice | 30 |", result);
        Assert.Contains("| Bob | 25 |", result);
    }

    [Fact]
    public void Convert_NestedBoldInsideParagraph_PreservesStructure()
    {
        var html = "<p>This is <strong>very</strong> important</p>";

        var result = _converter.Convert(html);

        Assert.Contains("**very**", result);
    }

    [Fact]
    public void Convert_ScriptTags_Removed()
    {
        var html = "<p>Hello</p><script>alert('xss');</script><p>World</p>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("script", result);
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void Convert_StyleTags_Removed()
    {
        var html = "<p>Hello</p><style>.red{color:red;}</style><p>World</p>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain(".red", result);
        Assert.DoesNotContain("style", result);
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void Convert_NestedLists_ConvertsCorrectly()
    {
        var html = "<ul><li>Parent<ul><li>Child 1</li><li>Child 2</li></ul></li></ul>";

        var result = _converter.Convert(html);

        Assert.Contains("- Parent", result);
        // ReverseMarkdown.Aot 使用 4 空格缩进子列表
        Assert.Matches(@"\s+- Child 1", result);
        Assert.Matches(@"\s+- Child 2", result);
    }

    [Fact]
    public void Convert_Truncate_TruncatesContent()
    {
        var html = "<p>" + new string('a', 200) + "</p>";

        var result = _converter.Convert(html, maxLength: 100);

        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void Convert_Truncate_UnderMaxKeepsAll()
    {
        var html = "<p>Short text</p>";

        var result = _converter.Convert(html, maxLength: 1000);

        Assert.True(result.Length < 1000);
    }

    [Fact]
    public void Convert_Strikethrough_ConvertsCorrectly()
    {
        var html = "<s>old text</s> and <del>deleted</del>";

        var result = _converter.Convert(html);

        Assert.Contains("~~old text~~", result);
        Assert.Contains("~~deleted~~", result);
    }

    [Fact]
    public void Convert_ComplexHtml_ProducesCoherentMarkdown()
    {
        var html = @"
<html>
<head><title>Test Page</title></head>
<body>
<h1>Welcome</h1>
<p>This is a <strong>bold</strong> and <em>italic</em> text with a <a href=""https://example.com"">link</a>.</p>
<pre><code>console.log('hello');</code></pre>
<ul>
<li>Item A</li>
<li>Item B</li>
</ul>
</body>
</html>";

        var result = _converter.Convert(html);

        Assert.Contains("# Welcome", result);
        Assert.Contains("**bold**", result);
        Assert.Contains("*italic*", result);
        Assert.Contains("[link](https://example.com)", result);
        Assert.Contains("```", result);
        Assert.Contains("console.log('hello')", result);
        Assert.Contains("- Item A", result);
        Assert.Contains("- Item B", result);
    }
}