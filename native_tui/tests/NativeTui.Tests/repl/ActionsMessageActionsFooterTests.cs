namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息操作条测试（消息操作域）— 对齐基准 messageActions.tsx MessageActionsFooter：
/// 上边框 dim ─ + 内容行动作段序列（key bold + ' ' label dim，段间 " · " dim）；
/// enter 段随 CanExpand/IsExpanded 切换文案、p 段随 PrimaryInputLabel 显隐；
/// MessageActionsState.Of 工厂从项类型投影（分组项可展开、映射内工具带标签）。
/// </summary>
public sealed class ActionsMessageActionsFooterTests
{
    /// <summary>工具调用假项（IToolCallInfo 契约 — ToolCallItem 接线前的状态投影验证载体）。</summary>
    private sealed record FakeToolCall(string ToolName, string Args) : IScrollItem, IToolCallInfo
    {
        /// <summary>内容高度（与 ToolCallItem 同形态 — 本测试不消费）。</summary>
        public int MeasureHeight(int width) => 2;

        /// <summary>不渲染（状态投影纯数据面）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>完整动作序列 — enter 展开 · c 复制 · p 复制 path · ↑↓ 导航 · esc 返回（任务指定文案）。</summary>
    [Fact]
    public void Render_FullActionSequence()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var bar = new MessageActionsFooter();
        bar.Render(screen, new Rect(2, 0, 56, MessageActionsFooter.Height), Theme.Dark,
            new MessageActionsState(CanExpand: true, IsExpanded: false, PrimaryInputLabel: "path"));

        Assert.Equal(new string('─', 56), RowText.Of(screen, 0, 2, 56));
        Assert.Equal("enter 展开 · c 复制 · p 复制 path · ↑↓ 导航 · esc 返回", RowText.Of(screen, 1, 2, 56).TrimEnd());
    }

    /// <summary>样式 — key bold（enter 列起）、label 与间隔 dim、上边框 dim。</summary>
    [Fact]
    public void Render_KeyBoldLabelDim()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var bar = new MessageActionsFooter();
        bar.Render(screen, new Rect(2, 0, 56, MessageActionsFooter.Height), Theme.Dark,
            new MessageActionsState(CanExpand: true, IsExpanded: false, PrimaryInputLabel: "path"));

        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(bold, screen[1, 2].StyleId);
        Assert.Equal(dim, screen[1, 7].StyleId);
        Assert.Equal(dim, screen[0, 2].StyleId);
    }

    /// <summary>IsExpanded — enter 段标签切"收起"。</summary>
    [Fact]
    public void Render_Expanded_LabelCollapses()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var bar = new MessageActionsFooter();
        bar.Render(screen, new Rect(2, 0, 56, MessageActionsFooter.Height), Theme.Dark,
            new MessageActionsState(CanExpand: true, IsExpanded: true, PrimaryInputLabel: null));

        Assert.Equal("enter 收起 · c 复制 · ↑↓ 导航 · esc 返回", RowText.Of(screen, 1, 2, 56).TrimEnd());
    }

    /// <summary>CanExpand=false — enter 段隐藏（普通消息选中形态）。</summary>
    [Fact]
    public void Render_WithoutExpand_SkipsEnterSegment()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var bar = new MessageActionsFooter();
        bar.Render(screen, new Rect(2, 0, 56, MessageActionsFooter.Height), Theme.Dark,
            new MessageActionsState(CanExpand: false, IsExpanded: false, PrimaryInputLabel: null));

        Assert.Equal("c 复制 · ↑↓ 导航 · esc 返回", RowText.Of(screen, 1, 2, 56).TrimEnd());
    }

    /// <summary>PrimaryInputLabel — command 标签随工具变化（p 复制 command）。</summary>
    [Fact]
    public void Render_CommandLabel_VariesByTool()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var bar = new MessageActionsFooter();
        bar.Render(screen, new Rect(2, 0, 56, MessageActionsFooter.Height), Theme.Dark,
            new MessageActionsState(CanExpand: false, IsExpanded: false, PrimaryInputLabel: "command"));

        Assert.Equal("c 复制 · p 复制 command · ↑↓ 导航 · esc 返回", RowText.Of(screen, 1, 2, 56).TrimEnd());
    }

    /// <summary>高度恒定 — 上边框 + 内容 2 行（footer 高度稳定语义）。</summary>
    [Fact]
    public void Height_IsAlwaysTwo()
    {
        Assert.Equal(2, MessageActionsFooter.Height);
    }

    /// <summary>Of 工厂 — 子代理分组项 → 可展开 + 展开态投影。</summary>
    [Fact]
    public void Of_SubagentGroup_CanExpandWithState()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        var state = MessageActionsState.Of(new SubagentGroupItem(coordinator));
        Assert.NotNull(state);
        Assert.True(state.CanExpand);
        Assert.False(state.IsExpanded);
        Assert.Null(state.PrimaryInputLabel);

        coordinator.IsExpanded = true;
        Assert.True(MessageActionsState.Of(new SubagentGroupItem(coordinator))!.IsExpanded);
    }

    /// <summary>Of 工厂 — 映射内工具调用项 → 主参数标签（无展开）。</summary>
    [Fact]
    public void Of_MappedToolCall_HasPrimaryLabel()
    {
        var state = MessageActionsState.Of(new FakeToolCall("Read", "{\"file_path\":\"a.cs\"}"));
        Assert.NotNull(state);
        Assert.False(state.CanExpand);
        Assert.Equal("path", state.PrimaryInputLabel);
    }

    /// <summary>Of 工厂 — 映射外工具 / 普通消息 / null → 无标签或 null。</summary>
    [Fact]
    public void Of_UnmappedToolOrPlainMessage_NoLabelOrNull()
    {
        Assert.Null(MessageActionsState.Of(new FakeToolCall("TodoWrite", "{}"))!.PrimaryInputLabel);
        Assert.Null(MessageActionsState.Of(new AssistantMessageItem("x"))!.PrimaryInputLabel);
        Assert.Null(MessageActionsState.Of(null));
    }
}
