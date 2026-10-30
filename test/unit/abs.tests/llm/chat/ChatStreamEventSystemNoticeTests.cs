namespace Abs.Tests.Llm.Chat;

/// <summary>
/// ChatStreamEvent.SystemNotice 消费契约（TASK078）— 中间件旁白事件此前落入 Switch/Match
/// default 分支抛 "Unknown event type: SystemNotice" 打死整个回合（用户实测 /?? 需求澄清失败）：
/// Switch 未提供回调时静默忽略（对齐 AgentStarted/SteerInput 既有先例 — 既有消费方零改动兼容）；
/// Match 显式契约（未提供回调抛引导性异常，对齐 AgentStarted 模式 — 静默 default(T) 是引用类型空值脚枪）。
/// </summary>
public sealed class ChatStreamEventSystemNoticeTests {
    [Fact]
    public void Switch_SystemNotice_NoCallback_DoesNotThrow() {
        var evt = ChatStreamEvent.SystemNotice("[系统提示] 已注入 OutputStyle 提示词");
        evt.Switch(
            onText: _ => throw new InvalidOperationException("不应路由到 onText"),
            onThinking: _ => throw new InvalidOperationException("不应路由到 onThinking"),
            onToolStart: (_, _, _) => throw new InvalidOperationException("不应路由到 onToolStart"),
            onToolEnd: (_, _, _, _, _) => throw new InvalidOperationException("不应路由到 onToolEnd"),
            onToolProgress: (_, _, _) => throw new InvalidOperationException("不应路由到 onToolProgress"),
            onLoopDetected: (_, _, _) => throw new InvalidOperationException("不应路由到 onLoopDetected"),
            onTimingSummary: _ => throw new InvalidOperationException("不应路由到 onTimingSummary"),
            onDone: (_, _) => throw new InvalidOperationException("不应路由到 onDone"));
    }

    [Fact]
    public void Switch_SystemNotice_WithCallback_InvokedWithContent() {
        string? received = null;
        var evt = ChatStreamEvent.SystemNotice("[系统提示] 旁白内容");
        evt.Switch(
            onText: _ => throw new InvalidOperationException("不应路由到 onText"),
            onThinking: _ => throw new InvalidOperationException("不应路由到 onThinking"),
            onToolStart: (_, _, _) => throw new InvalidOperationException("不应路由到 onToolStart"),
            onToolEnd: (_, _, _, _, _) => throw new InvalidOperationException("不应路由到 onToolEnd"),
            onToolProgress: (_, _, _) => throw new InvalidOperationException("不应路由到 onToolProgress"),
            onLoopDetected: (_, _, _) => throw new InvalidOperationException("不应路由到 onLoopDetected"),
            onTimingSummary: _ => throw new InvalidOperationException("不应路由到 onTimingSummary"),
            onDone: (_, _) => throw new InvalidOperationException("不应路由到 onDone"),
            onSystemNotice: content => received = content);
        Assert.Equal("[系统提示] 旁白内容", received);
    }

    [Fact]
    public void Match_SystemNotice_NoCallback_ThrowsGuided() {
        var evt = ChatStreamEvent.SystemNotice("[系统提示] 旁白内容");
        var ex = Assert.Throws<InvalidOperationException>(() => evt.Match(
            onText: _ => "text",
            onThinking: _ => "thinking",
            onToolStart: (_, _, _) => "toolStart",
            onToolEnd: (_, _, _, _, _) => "toolEnd",
            onToolProgress: (_, _, _) => "toolProgress",
            onLoopDetected: (_, _, _) => "loop",
            onTimingSummary: _ => "timing",
            onDone: (_, _) => "done"));
        Assert.Contains("onSystemNotice", ex.Message);
    }

    [Fact]
    public void Match_SystemNotice_WithCallback_ReturnsResult() {
        var evt = ChatStreamEvent.SystemNotice("[系统提示] 旁白内容");
        var result = evt.Match(
            onText: _ => "text",
            onThinking: _ => "thinking",
            onToolStart: (_, _, _) => "toolStart",
            onToolEnd: (_, _, _, _, _) => "toolEnd",
            onToolProgress: (_, _, _) => "toolProgress",
            onLoopDetected: (_, _, _) => "loop",
            onTimingSummary: _ => "timing",
            onDone: (_, _) => "done",
            onSystemNotice: content => $"notice:{content}");
        Assert.Equal("notice:[系统提示] 旁白内容", result);
    }
}
