namespace McpToolRegistry.Tests;

public class McpResultCollapseClassifierTests
{
    [Fact]
    public void Classify_NullResult_ReturnsNoneCategory()
    {
        var result = McpResultCollapseClassifier.Classify(null!);
        result.Category.Should().Be(CollapseCategory.None);
        result.ShouldCollapse.Should().BeFalse();
        result.Priority.Should().Be(0);
    }

    [Fact]
    public void Classify_ErrorResult_ReturnsErrorCategory()
    {
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = "something went wrong" }],
            IsError = true
        };

        var classification = McpResultCollapseClassifier.Classify(result);
        classification.Category.Should().Be(CollapseCategory.Error);
        classification.Priority.Should().Be(100);
        classification.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void Classify_LongErrorResult_ShouldCollapse()
    {
        var longError = new string('x', 300);
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = longError }],
            IsError = true
        };

        var classification = McpResultCollapseClassifier.Classify(result);
        classification.Category.Should().Be(CollapseCategory.Error);
        classification.ShouldCollapse.Should().BeTrue();
    }

    [Fact]
    public void ClassifyText_NullText_ReturnsNoneCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText(null);
        result.Category.Should().Be(CollapseCategory.None);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_EmptyText_ReturnsNoneCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("");
        result.Category.Should().Be(CollapseCategory.None);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_WhitespaceOnly_ReturnsNoneCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("   \n\t  ");
        result.Category.Should().Be(CollapseCategory.None);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_ShortText_ReturnsShortTextCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("hello world");
        result.Category.Should().Be(CollapseCategory.ShortText);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_ShortTextUnder200Chars_NoCollapse()
    {
        var text = new string('a', 200);
        var result = McpResultCollapseClassifier.ClassifyText(text);
        result.Category.Should().Be(CollapseCategory.ShortText);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_MediumTextOver200CharsUnder2000_NoCollapse()
    {
        var text = new string('a', 500);
        var result = McpResultCollapseClassifier.ClassifyText(text);
        result.Category.Should().Be(CollapseCategory.ShortText);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyText_LongTextOver2000Chars_ShouldCollapse()
    {
        var text = new string('a', 2500);
        var result = McpResultCollapseClassifier.ClassifyText(text);
        result.Category.Should().Be(CollapseCategory.LongText);
        result.ShouldCollapse.Should().BeTrue();
        result.CollapseTitle.Should().Contain("长文本");
    }

    [Fact]
    public void ClassifyText_JsonData_ReturnsJsonCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("{\"key\": \"value\"}");
        result.Category.Should().Be(CollapseCategory.JsonData);
        result.Priority.Should().Be(80);
    }

    [Fact]
    public void ClassifyText_JsonArray_ReturnsJsonCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("[1, 2, 3]");
        result.Category.Should().Be(CollapseCategory.JsonData);
        result.Priority.Should().Be(80);
    }

    [Fact]
    public void ClassifyText_InvalidJson_ReturnsNotJsonCategory()
    {
        var result = McpResultCollapseClassifier.ClassifyText("this is not json at all");
        result.Category.Should().NotBe(CollapseCategory.JsonData);
    }

    [Fact]
    public void ClassifyText_CodeBlock_ReturnsCodeBlockCategory()
    {
        var code = "```python\nprint('hello')\n```";
        var result = McpResultCollapseClassifier.ClassifyText(code);
        result.Category.Should().Be(CollapseCategory.CodeBlock);
        result.Priority.Should().Be(70);
        result.CollapseTitle.Should().Be("代码块");
    }

    [Fact]
    public void ClassifyText_TableData_ReturnsTableCategory()
    {
        var table = "| Name | Age |\n| --- | --- |\n| Alice | 30 |\n| Bob | 25 |";
        var result = McpResultCollapseClassifier.ClassifyText(table);
        result.Category.Should().Be(CollapseCategory.TableData);
        result.Priority.Should().Be(60);
    }

    [Fact]
    public void ClassifyText_ListData_ReturnsListCategory()
    {
        var listText = string.Join("\n", Enumerable.Range(0, 12).Select(i => $"- item {i}"));
        var result = McpResultCollapseClassifier.ClassifyText(listText);
        result.Category.Should().Be(CollapseCategory.ListData);
        result.Priority.Should().Be(50);
    }

    [Fact]
    public void ClassifyText_FewListItems_NotListCategory()
    {
        var listText = "- item 1\n- item 2\n- item 3";
        var result = McpResultCollapseClassifier.ClassifyText(listText);
        result.Category.Should().NotBe(CollapseCategory.ListData);
    }

    [Fact]
    public void ClassifyText_NumberedList_ReturnsListCategory()
    {
        var listText = string.Join("\n", Enumerable.Range(0, 12).Select(i => $"{i + 1}. item {i}"));
        var result = McpResultCollapseClassifier.ClassifyText(listText);
        result.Category.Should().Be(CollapseCategory.ListData);
    }

    [Fact]
    public void ClassifyBinary_ImageMimeType_ReturnsImageDataCategory()
    {
        var data = new byte[1024];
        var result = McpResultCollapseClassifier.ClassifyBinary(data, "image/png");
        result.Category.Should().Be(CollapseCategory.ImageData);
        result.Priority.Should().Be(90);
    }

    [Fact]
    public void ClassifyBinary_LargeImage_ShouldCollapse()
    {
        var data = new byte[1024 * 1024 + 1];
        var result = McpResultCollapseClassifier.ClassifyBinary(data, "image/png");
        result.Category.Should().Be(CollapseCategory.ImageData);
        result.ShouldCollapse.Should().BeTrue();
    }

    [Fact]
    public void ClassifyBinary_SmallImage_NoCollapse()
    {
        var data = new byte[512];
        var result = McpResultCollapseClassifier.ClassifyBinary(data, "image/jpeg");
        result.Category.Should().Be(CollapseCategory.ImageData);
        result.ShouldCollapse.Should().BeFalse();
    }

    [Fact]
    public void ClassifyBinary_NonImageMimeType_ReturnsBinaryDataCategory()
    {
        var data = new byte[100];
        var result = McpResultCollapseClassifier.ClassifyBinary(data, "application/pdf");
        result.Category.Should().Be(CollapseCategory.BinaryData);
        result.Priority.Should().Be(85);
        result.ShouldCollapse.Should().BeTrue();
    }

    [Fact]
    public void ClassifyBinary_NullMimeType_ReturnsBinaryDataCategory()
    {
        var data = new byte[100];
        var result = McpResultCollapseClassifier.ClassifyBinary(data, null);
        result.Category.Should().Be(CollapseCategory.BinaryData);
    }

    [Fact]
    public void ClassifyBinary_NullData_ReturnsBinaryDataCategory()
    {
        byte[]? data = null;
        var result = McpResultCollapseClassifier.ClassifyBinary(data!, null);
        result.Category.Should().Be(CollapseCategory.BinaryData);
    }

    [Fact]
    public void ClassifyBatch_MultipleResults_ReturnsClassificationsForAll()
    {
        var results = new Dictionary<string, ToolResult>
        {
            ["tool1"] = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "short" }],
                IsError = false
            },
            ["tool2"] = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = new string('x', 2500) }],
                IsError = false
            }
        };

        var batch = McpResultCollapseClassifier.ClassifyBatch(results);
        batch.Should().ContainKey("tool1");
        batch.Should().ContainKey("tool2");
        batch["tool1"].Category.Should().Be(CollapseCategory.ShortText);
        batch["tool2"].Category.Should().Be(CollapseCategory.LongText);
    }

    [Fact]
    public void Classify_TextContentWithMultipleParts_CombinesText()
    {
        var result = new ToolResult
        {
            Content =
            [
                new ToolContent { Type = ToolContentType.Text, Text = "part1" },
                new ToolContent { Type = ToolContentType.Text, Text = "part2" }
            ],
            IsError = false
        };

        var classification = McpResultCollapseClassifier.Classify(result);
        classification.Category.Should().Be(CollapseCategory.ShortText);
    }

    [Fact]
    public void Classify_NonTextContentIgnored()
    {
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Image, Text = "image_data" }],
            IsError = false
        };

        var classification = McpResultCollapseClassifier.Classify(result);
        classification.Category.Should().Be(CollapseCategory.None);
    }

    [Fact]
    public void ClassifyText_LongTextWithManyLines_ShouldCollapse()
    {
        var lines = Enumerable.Range(0, 35).Select(_ => "line of text").ToList();
        var text = string.Join("\n", lines);
        var result = McpResultCollapseClassifier.ClassifyText(text);
        result.ShouldCollapse.Should().BeTrue();
    }

    [Fact]
    public void ClassifyText_JsonDataLong_ShouldCollapse()
    {
        var longValue = new string('x', 300);
        var json = $"{{\"key\": \"{longValue}\"}}";
        var result = McpResultCollapseClassifier.ClassifyText(json);
        result.Category.Should().Be(CollapseCategory.JsonData);
        result.ShouldCollapse.Should().BeTrue();
    }

    [Fact]
    public void ClassifyText_JsonDataShort_NoCollapse()
    {
        var result = McpResultCollapseClassifier.ClassifyText("{\"k\":\"v\"}");
        result.Category.Should().Be(CollapseCategory.JsonData);
        result.ShouldCollapse.Should().BeFalse();
    }
}
