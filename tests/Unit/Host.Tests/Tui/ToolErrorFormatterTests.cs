namespace Host.Tests.Tui;

/// <summary>
/// ToolErrorFormatter 单元测试 — 验证 &lt;tool_use_error&gt; 标签解析为友好消息。
/// 回归背景：SchemaValidationMiddleware 把错误包在 &lt;tool_use_error&gt; 标签里，
/// 直接显示让用户看到 XML 标签，需解析为纯净错误消息。
/// </summary>
public class ToolErrorFormatterTests
{
    [Fact]
    public void ExtractMessage_含tool_use_error标签_提取标签内容()
    {
        var text = "<tool_use_error>InputValidationError: The parameter `questions` type is expected as `array` but provided as `string`</tool_use_error>";

        var result = ToolErrorFormatter.ExtractMessage(text, isError: true);

        result.Should().NotContain("<tool_use_error>");
        result.Should().NotContain("</tool_use_error>");
        result.Should().Contain("questions");
        result.Should().Contain("array");
        result.Should().Contain("string");
    }

    [Fact]
    public void ExtractMessage_无标签_返回原文()
    {
        var text = "权限不足：该操作被拒绝";

        var result = ToolErrorFormatter.ExtractMessage(text, isError: true);

        result.Should().Be("权限不足：该操作被拒绝");
    }

    [Fact]
    public void ExtractMessage_非错误_返回原文不解析标签()
    {
        var text = "<tool_use_error>不应被解析</tool_use_error>";

        var result = ToolErrorFormatter.ExtractMessage(text, isError: false);

        result.Should().Be(text);
    }

    [Fact]
    public void ExtractMessage_空文本_返回空()
    {
        ToolErrorFormatter.ExtractMessage(null, isError: true).Should().BeEmpty();
        ToolErrorFormatter.ExtractMessage("", isError: true).Should().BeEmpty();
    }

    [Fact]
    public void ExtractMessage_只有开始标签_返回开始标签后内容()
    {
        var text = "<tool_use_error>缺少结束标签的错误消息";

        var result = ToolErrorFormatter.ExtractMessage(text, isError: true);

        result.Should().Be("缺少结束标签的错误消息");
    }
}
