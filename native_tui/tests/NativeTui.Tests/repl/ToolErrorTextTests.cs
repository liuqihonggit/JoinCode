namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具错误文本归一测试（TASK069-M5 — 对齐基准 components/FallbackToolUseErrorMessage.tsx L38-56）：
/// trim 后已带 `Error: ` / `Cancelled: ` 前缀则保留，否则补 `Error: `；空内容得裸前缀。
/// </summary>
public sealed class ToolErrorTextTests
{
    [Fact]
    public void PlainText_GetsErrorPrefix()
    {
        Assert.Equal("Error: command failed", ToolErrorText.Normalize("command failed"));
    }

    [Fact]
    public void ExistingErrorPrefix_NotDuplicated()
    {
        Assert.Equal("Error: boom", ToolErrorText.Normalize("Error: boom"));
    }

    [Fact]
    public void ExistingCancelledPrefix_NotOverridden()
    {
        Assert.Equal("Cancelled: user interrupt", ToolErrorText.Normalize("Cancelled: user interrupt"));
    }

    [Fact]
    public void SurroundingWhitespace_TrimmedBeforePrefix()
    {
        Assert.Equal("Error: boom", ToolErrorText.Normalize("\n  boom \n"));
    }

    [Fact]
    public void MultiLine_PrefixOnlyOnFirstLine()
    {
        Assert.Equal("Error: line1\nline2", ToolErrorText.Normalize("line1\nline2"));
    }

    [Fact]
    public void EmptyContent_LeavesBarePrefix()
    {
        Assert.Equal(ToolErrorText.ErrorPrefix, ToolErrorText.Normalize(null));
        Assert.Equal(ToolErrorText.ErrorPrefix, ToolErrorText.Normalize("   "));
    }

    [Fact]
    public void LowercaseErrorPrefix_StillPrefixed()
    {
        // 基准 startsWith 区分大小写 — 小写 error: 会再补一次前缀
        Assert.Equal("Error: error: boom", ToolErrorText.Normalize("error: boom"));
    }
}
