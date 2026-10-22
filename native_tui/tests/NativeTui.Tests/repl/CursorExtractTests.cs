namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息光标提取扩展测试（消息操作域）— MessageCursor.ExtractFullText/ExtractPrimaryInput：
/// 光标选中项的可复制文本数据面（消费 CopyTextExtractor / PrimaryInputMap）；
/// 未激活/越界/不可复制类型 null（消息动态增删安全防御）。
/// ToolCallItem 的 IToolCallInfo 接线在 item 域（S2，见未决问题）— 本文件以 FakeToolCall 验证提取链路。
/// </summary>
public sealed class CursorExtractTests
{
    /// <summary>工具调用假项（IToolCallInfo 契约 — ToolCallItem 接线前的提取链路验证载体）。</summary>
    private sealed record FakeToolCall(string ToolName, string Args) : IScrollItem, ICursorNavigable, IToolCallInfo
    {
        /// <summary>内容高度（与 ToolCallItem 同形态 — 本测试不消费）。</summary>
        public int MeasureHeight(int width) => 2;

        /// <summary>不渲染（提取链路纯数据面）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>构造混合消息序列（q1[0] / tool[1] / group[2] / q2[3] — 全部可导航）。</summary>
    private static IScrollItem[] CreateItems(SubagentGroupCoordinator? group = null)
    {
        group ??= CreateGroup();
        return
        [
            new UserMessageItem("<system-reminder>env</system-reminder>q1"),
            new FakeToolCall("Bash", "{\"command\":\"dotnet test\"}"),
            new SubagentGroupItem(group),
            new UserMessageItem("q2"),
        ];
    }

    /// <summary>构造单代理组（无活动 — 展开拼接为状态行）。</summary>
    private static SubagentGroupCoordinator CreateGroup()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        return coordinator;
    }

    /// <summary>Begin 定位最后用户消息 — 提取剥 system-reminder 后原文。</summary>
    [Fact]
    public void ExtractFullText_SelectedUserMessage_StripsReminders()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        Assert.Equal(3, cursor.SelectedIndex);

        Assert.Equal("q2", cursor.ExtractFullText(items));
    }

    /// <summary>选中工具调用 — 提取主参数值（基准 assistant tool_use 分支语义）。</summary>
    [Fact]
    public void ExtractFullText_SelectedToolCall_ReturnsPrimaryInput()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        cursor.Move(items, -1);
        cursor.Move(items, -1);
        Assert.Equal(1, cursor.SelectedIndex);

        Assert.Equal("dotnet test", cursor.ExtractFullText(items));
    }

    /// <summary>选中子代理分组 — 提取逐条目拼接文本。</summary>
    [Fact]
    public void ExtractFullText_SelectedGroup_JoinedEntries()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        cursor.Move(items, -1);
        Assert.Equal(2, cursor.SelectedIndex);

        Assert.Equal("explore (搜索) · 0 tool uses · 0 tokens", cursor.ExtractFullText(items));
    }

    /// <summary>ExtractPrimaryInput — 选中映射内工具调用返回主参数值。</summary>
    [Fact]
    public void ExtractPrimaryInput_SelectedToolCall_ReturnsValue()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        cursor.Move(items, -1);
        cursor.Move(items, -1);

        Assert.Equal("dotnet test", cursor.ExtractPrimaryInput(items));
    }

    /// <summary>ExtractPrimaryInput — 选中非工具调用项（用户消息/分组）返回 null。</summary>
    [Fact]
    public void ExtractPrimaryInput_NonToolCall_ReturnsNull()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        Assert.Null(cursor.ExtractPrimaryInput(items));

        cursor.Move(items, -1);
        Assert.Null(cursor.ExtractPrimaryInput(items));
    }

    /// <summary>未激活 — 两提取器均 null。</summary>
    [Fact]
    public void Extract_InactiveCursor_ReturnsNull()
    {
        var cursor = new MessageCursor();
        Assert.Null(cursor.ExtractFullText(CreateItems()));
        Assert.Null(cursor.ExtractPrimaryInput(CreateItems()));
    }

    /// <summary>选中索引越界（消息动态删空防御）— null。</summary>
    [Fact]
    public void Extract_SelectedIndexOutOfRange_ReturnsNull()
    {
        var cursor = new MessageCursor();
        cursor.Begin(CreateItems());
        Assert.True(cursor.IsCursorActive);

        var shrunk = new IScrollItem[] { new UserMessageItem("仅剩一条") };
        Assert.Null(cursor.ExtractFullText(shrunk));
        Assert.Null(cursor.ExtractPrimaryInput(shrunk));
    }

    /// <summary>Exit 后 — 两提取器均 null。</summary>
    [Fact]
    public void Extract_AfterExit_ReturnsNull()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        cursor.Exit();

        Assert.Null(cursor.ExtractFullText(items));
        Assert.Null(cursor.ExtractPrimaryInput(items));
    }
}
