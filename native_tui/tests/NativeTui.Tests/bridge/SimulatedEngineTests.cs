namespace NativeTui.Tests.Bridge;

/// <summary>
/// SimulatedEngine 单元测试 — Demo 演示命令路由（md/diff/perm/all — 未知命令 Error 诱导）+ 时钟注入确定性验证：
/// 有效命令立即产 TurnStarted、时间推进按脚本产阶段事件（ToolCall/ToolResult/TokenDelta/PermissionRequest/TurnCompleted）、
/// Interrupt 忙时打断、忙时提交拒绝（Error 诱导）、权限模式本地回执统一走事件。
/// </summary>
public sealed class SimulatedEngineTests
{
    /// <summary>虚拟时钟 — 手动推进毫秒，驱动状态机确定性跳段。</summary>
    private sealed class VirtualClock
    {
        private long _ms;

        /// <summary>当前时刻（ms）。</summary>
        public long Now => _ms;

        /// <summary>推进指定毫秒。</summary>
        public void Advance(long ms) => _ms += ms;
    }

    /// <summary>Submit 后首拍排空：立即产出 TurnStarted。</summary>
    [Fact]
    public void Submit_Drains_TurnStartedImmediately()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("all");
        var events = engine.DrainEvents();
        Assert.Equal([new BridgeEvent.TurnStarted()], events);
    }

    /// <summary>虚拟时钟 1.3s：进入 ToolUse 阶段，产出 ToolCall 事件（Edit — 演示 StructuredDiff 链路）。</summary>
    [Fact]
    public void Advance_ToToolUse_Emits_ToolCall()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(1300);
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.ToolCall { Name: "Edit" });
    }

    /// <summary>虚拟时钟 2.5s：进入 Responding 阶段，产出 ToolResult（带结构化补丁）+ TokenDelta。</summary>
    [Fact]
    public void Advance_ToResponding_Emits_ToolResultWithPatchAndTokenDelta()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(2500);
        var events = engine.DrainEvents();
        Assert.Contains(events, e => e is BridgeEvent.ToolResult);
        var result = Assert.IsType<BridgeEvent.ToolResult>(events.First(e => e is BridgeEvent.ToolResult));
        Assert.NotNull(result.Patch);
        Assert.All(result.Patch, hunk => Assert.False(string.IsNullOrEmpty(hunk.Header)));
        // M8-1：进入 Responding 即开始逐字流式 — 首拍发带文本增量（预分片首段）
        Assert.Contains(events, e => e is BridgeEvent.TokenDelta { Length: > 0, Text: { Length: > 0 } });
    }

    /// <summary>虚拟时钟 5.5s：回合完成，产出 AssistantMessage + TurnCompleted，后续拍空。</summary>
    [Fact]
    public void Advance_ToCompletion_Emits_AssistantMessageAndTurnCompleted()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(5500);
        var events = engine.DrainEvents();
        Assert.Contains(events, e => e is BridgeEvent.AssistantMessage { Text.Length: > 0 });
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);

        Assert.Empty(engine.DrainEvents());
    }

    /// <summary>虚拟时钟 5.5s：完成事件携带 Markdown 块模型（md 演示命令 — 全形态 8 类块）。</summary>
    [Fact]
    public void Advance_ToCompletion_Emits_AssistantMessageWithMarkdown()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("md");
        engine.DrainEvents();

        clock.Advance(5500);
        var events = engine.DrainEvents();
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(events.First(e => e is BridgeEvent.AssistantMessage));
        Assert.NotNull(message.Markdown);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownHeading);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownParagraph);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownList { Ordered: false });
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownList { Ordered: true });
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownQuote);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownCode);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownRule);
        Assert.Contains(message.Markdown.Blocks, b => b is MarkdownTable);
    }

    /// <summary>md 命令：纯 Markdown 演示 — 全程无工具事件（ToolCall/ToolResult）。</summary>
    [Fact]
    public void Submit_MdCommand_NoToolEvents()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("md");
        engine.DrainEvents();

        clock.Advance(5500);
        var events = engine.DrainEvents();
        Assert.DoesNotContain(events, e => e is BridgeEvent.ToolCall);
        Assert.DoesNotContain(events, e => e is BridgeEvent.ToolResult);
    }

    /// <summary>diff 命令：纯 StructuredDiff 演示 — ToolResult 带补丁，完成消息为纯文本（Markdown null）。</summary>
    [Fact]
    public void Submit_DiffCommand_PatchWithoutMarkdown()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("diff");
        engine.DrainEvents();

        clock.Advance(2500);
        var midEvents = engine.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(midEvents.First(e => e is BridgeEvent.ToolResult));
        Assert.NotNull(result.Patch);

        clock.Advance(3000);
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(engine.DrainEvents().First(e => e is BridgeEvent.AssistantMessage));
        Assert.Null(message.Markdown);
    }

    /// <summary>perm 命令：1.2s 产出 PermissionRequest 事件（工具名/描述非空 — 权限对话框演示）。</summary>
    [Fact]
    public void Submit_PermCommand_EmitsPermissionRequest()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("perm");
        engine.DrainEvents();

        clock.Advance(1300);
        var request = Assert.IsType<BridgeEvent.PermissionRequest>(engine.DrainEvents().First(e => e is BridgeEvent.PermissionRequest));
        Assert.False(string.IsNullOrEmpty(request.ToolName));
        Assert.False(string.IsNullOrEmpty(request.Description));
    }

    /// <summary>perm 命令：挂起期无 token 流式（权限等待语义），8s 延迟完成（交互窗口加长）。</summary>
    [Fact]
    public void Submit_PermCommand_NoTokenDeltaUntilLateCompletion()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("perm");
        engine.DrainEvents();

        clock.Advance(3000);
        var events = engine.DrainEvents();
        Assert.DoesNotContain(events, e => e is BridgeEvent.TokenDelta);
        Assert.DoesNotContain(events, e => e is BridgeEvent.TurnCompleted);

        clock.Advance(5100);
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.TurnCompleted);
    }

    /// <summary>all 命令：完整回合 — 补丁与 Markdown 齐全（既有默认演示）。</summary>
    [Fact]
    public void Submit_AllCommand_PatchAndMarkdownTogether()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(2500);
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.ToolResult { Patch: not null });

        clock.Advance(3000);
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(engine.DrainEvents().First(e => e is BridgeEvent.AssistantMessage));
        Assert.NotNull(message.Markdown);
    }

    /// <summary>未知命令：拒绝并回 Error 诱导（列出全部演示命令 — 含为什么/怎么做）。</summary>
    [Theory]
    [InlineData("随便聊聊")]
    [InlineData("hello")]
    public void Submit_UnknownCommand_RejectsWithGuidance(string input)
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit(input);
        var events = engine.DrainEvents();

        var error = Assert.IsType<BridgeEvent.Error>(events.Single());
        Assert.Contains("md", error.Message);
        Assert.Contains("diff", error.Message);
        Assert.Contains("perm", error.Message);
        Assert.Contains("all", error.Message);
        Assert.DoesNotContain(events, e => e is BridgeEvent.TurnStarted);
    }

    /// <summary>斜杠前缀命令（TUI 补全接受形态）：/md 等价 md 路由 — TurnStarted 而非 Error。</summary>
    [Theory]
    [InlineData("/md")]
    [InlineData("/all")]
    public void Submit_SlashPrefixedCommand_RoutesLikeBareCommand(string input)
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit(input);
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>斜杠前缀带尾随空格（Tab 接受形态 "/cmd "）：trim 后等价路由。</summary>
    [Fact]
    public void Submit_SlashCommandWithTrailingSpace_RoutesAfterTrim()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("/md ");
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>忙时 Interrupt：产出 TurnInterrupted，回合终止。</summary>
    [Fact]
    public void Interrupt_WhileBusy_Emits_TurnInterrupted()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("all");
        engine.DrainEvents();

        engine.Interrupt();
        Assert.Equal([new BridgeEvent.TurnInterrupted()], engine.DrainEvents());
        Assert.Empty(engine.DrainEvents());
    }

    /// <summary>忙时再次 Submit：拒绝并回 Error 诱导（对齐 EngineTurnClient 忙时语义）。</summary>
    [Fact]
    public void Submit_WhileBusy_RejectsWithError()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("all");
        engine.DrainEvents();

        engine.Submit("all");
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.Error { Message.Length: > 0 });
    }

    /// <summary>打断后再 Submit：可开始新回合（状态机复位）。</summary>
    [Fact]
    public void Submit_AfterInterrupt_StartsNewTurn()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("all");
        engine.DrainEvents();
        engine.Interrupt();
        engine.DrainEvents();

        engine.Submit("all");
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>SetPermissionMode：本地立即回执 PermissionModeChanged（统一事件语义）。</summary>
    [Fact]
    public void SetPermissionMode_Emits_PermissionModeChanged()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.SetPermissionMode("plan");
        Assert.Equal([new BridgeEvent.PermissionModeChanged("plan")], engine.DrainEvents());
    }

    /// <summary>IsAlive 恒 true + DrainDiagnostics 默认空（接口默认实现）。</summary>
    [Fact]
    public void AliveForever_And_NoDiagnostics()
    {
        var engine = new SimulatedEngine(static () => 0L);
        Assert.True(engine.IsAlive);
        Assert.Empty(((IReplEngine)engine).DrainDiagnostics());
    }

    /// <summary>闲时 Interrupt：无回合可打断，no-op 不产事件。</summary>
    [Fact]
    public void Interrupt_WhileIdle_IsNoOp()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Interrupt();
        Assert.Empty(engine.DrainEvents());
    }

    // ==== G3 图片附件通道（E4 引擎协议 — 计数 + 原文本路由）====

    /// <summary>图片重载提交 — 附件计数累加 + 原文本路由单参（演示验证：md 命令照常 TurnStarted；
    /// 回合间用 Interrupt 复位保持闲态 — 忙时二次提交会被拒绝）。</summary>
    [Fact]
    public void Submit_WithImages_CountsAttachmentsAndRoutesText()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("md", [
            new BridgeImageAttachment("QUJD", "image/png", "a.png"),
            new BridgeImageAttachment("QkM=", "image/jpeg", "b.jpg"),
        ]);
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());

        engine.Interrupt();
        engine.DrainEvents();
        engine.Submit("md", [new BridgeImageAttachment("QUJD", "image/png", "a.png")]);

        // 计数跨提交累加（2 + 1 = 3）+ 文本路由不受附件影响
        Assert.Equal(3, engine.SubmittedImageCount);
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>图片重载 null/空附件 — 等价单参提交不计数（接口默认成员语义对齐）。</summary>
    [Fact]
    public void Submit_WithNullImages_EquivalentToSingleParameter()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("md", images: null);

        Assert.Equal(0, engine.SubmittedImageCount);
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    // ==== G3 新会话（D8 Clear — 回合状态复位演示语义）====

    /// <summary>StartNewConversation：在途回合中止（不再产阶段事件）+ 状态复位（后续提交开新回合）。</summary>
    [Fact]
    public void StartNewConversation_AbortsInFlightTurnAndResetsState()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        engine.StartNewConversation();
        clock.Advance(5500);

        // 回合已复位 — 时间推进不产完成事件（旧回合终止）
        Assert.DoesNotContain(engine.DrainEvents(), e => e is BridgeEvent.TurnCompleted);

        // 状态复位 — 可开新回合（TurnStarted 即新脚本起点）
        engine.Submit("md");
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>StartNewConversation：事件队列保序不清（在途 Error 诱导等已产出事件仍可达下拍排空）。</summary>
    [Fact]
    public void StartNewConversation_KeepsPendingEventsDrainable()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("未知命令");

        engine.StartNewConversation();

        // 未知命令 Error 诱导不被新会话吞 — 下拍照常排空
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.Error);
    }
}
