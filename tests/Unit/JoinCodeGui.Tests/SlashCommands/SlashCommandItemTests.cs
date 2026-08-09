using FluentAssertions;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// SlashCommandItem 单元测试 — 验证 ToolTipText 悬停提示文本的派生逻辑。
/// </summary>
public class SlashCommandItemTests
{
    private static SlashCommandItem Item(string usage) =>
        new() { Name = "/test", Description = "测试", Usage = usage };

    [Fact]
    public void ToolTipText_ReturnsUsage_WhenUsageNonEmpty()
    {
        var item = Item("/test [arg]");

        item.ToolTipText.Should().Be("/test [arg]");
    }

    [Fact]
    public void ToolTipText_ReturnsNull_WhenUsageEmpty()
    {
        var item = Item(string.Empty);

        item.ToolTipText.Should().BeNull();
    }

    [Fact]
    public void ToolTipText_ReturnsNull_WhenUsageWhitespace()
    {
        var item = Item("   ");

        item.ToolTipText.Should().BeNull();
    }
}
