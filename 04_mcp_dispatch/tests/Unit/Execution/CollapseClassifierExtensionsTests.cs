namespace McpToolRegistry.Tests;

public class CollapseClassifierExtensionsTests
{
    [Fact]
    public void GetCollapseRecommendation_NoCollapse_ReturnsNoNeed()
    {
        var classification = new CollapseClassification(CollapseCategory.ShortText, false, 10);
        classification.GetCollapseRecommendation().Should().Be("无需折叠");
    }

    [Fact]
    public void GetCollapseRecommendation_LongText_ReturnsLongTextRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.LongText, true, 40);
        classification.GetCollapseRecommendation().Should().Be("建议折叠长文本内容");
    }

    [Fact]
    public void GetCollapseRecommendation_CodeBlock_ReturnsCodeBlockRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.CodeBlock, true, 70);
        classification.GetCollapseRecommendation().Should().Be("建议折叠代码块");
    }

    [Fact]
    public void GetCollapseRecommendation_JsonData_ReturnsJsonRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.JsonData, true, 80);
        classification.GetCollapseRecommendation().Should().Be("建议折叠 JSON 数据");
    }

    [Fact]
    public void GetCollapseRecommendation_ListData_ReturnsListRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.ListData, true, 50);
        classification.GetCollapseRecommendation().Should().Be("建议折叠列表数据");
    }

    [Fact]
    public void GetCollapseRecommendation_TableData_ReturnsTableRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.TableData, true, 60);
        classification.GetCollapseRecommendation().Should().Be("建议折叠表格数据");
    }

    [Fact]
    public void GetCollapseRecommendation_Error_ReturnsErrorRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.Error, true, 100);
        classification.GetCollapseRecommendation().Should().Be("错误信息已折叠");
    }

    [Fact]
    public void GetCollapseRecommendation_BinaryData_ReturnsBinaryRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.BinaryData, true, 85);
        classification.GetCollapseRecommendation().Should().Be("二进制数据已折叠");
    }

    [Fact]
    public void GetCollapseRecommendation_ImageData_ReturnsImageRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.ImageData, true, 90);
        classification.GetCollapseRecommendation().Should().Be("图像数据已折叠");
    }

    [Fact]
    public void GetCollapseRecommendation_NoneWithCollapse_ReturnsGenericRecommendation()
    {
        var classification = new CollapseClassification(CollapseCategory.None, true, 0);
        classification.GetCollapseRecommendation().Should().Be("建议折叠");
    }

    [Fact]
    public void GetPriorityDescription_VeryHigh_ReturnsVeryHigh()
    {
        var classification = new CollapseClassification(CollapseCategory.Error, true, 95);
        classification.GetPriorityDescription().Should().Be("极高");
    }

    [Fact]
    public void GetPriorityDescription_High_ReturnsHigh()
    {
        var classification = new CollapseClassification(CollapseCategory.CodeBlock, true, 70);
        classification.GetPriorityDescription().Should().Be("高");
    }

    [Fact]
    public void GetPriorityDescription_Medium_ReturnsMedium()
    {
        var classification = new CollapseClassification(CollapseCategory.ListData, true, 50);
        classification.GetPriorityDescription().Should().Be("中");
    }

    [Fact]
    public void GetPriorityDescription_Low_ReturnsLow()
    {
        var classification = new CollapseClassification(CollapseCategory.LongText, true, 35);
        classification.GetPriorityDescription().Should().Be("低");
    }

    [Fact]
    public void GetPriorityDescription_VeryLow_ReturnsVeryLow()
    {
        var classification = new CollapseClassification(CollapseCategory.ShortText, false, 10);
        classification.GetPriorityDescription().Should().Be("极低");
    }
}
