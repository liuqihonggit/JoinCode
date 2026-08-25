using System.Collections.ObjectModel;

using JoinCode.Abstractions.LLM.Chat;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// ChatTurnProcessor 单元测试 — 从 MainViewModel 抽取的回合组装器契约。
/// 覆盖：占位创建/流式缓冲/思考空泡移除/工具卡片顺序/token 聚合/子代理组卡片路由。
/// </summary>
public class ChatTurnProcessorTests
{
    private static (ChatTurnProcessor P, ObservableCollection<ChatUiMessage> M) Create()
    {
        var messages = new ObservableCollection<ChatUiMessage>();
        var processor = new ChatTurnProcessor(messages);
        processor.BeginTurn();
        return (processor, messages);
    }

    [Fact]
    public void BeginTurn_ShouldAddStreamingAssistantPlaceholder()
    {
        var (p, m) = Create();
        m.Should().ContainSingle(x => x.IsStreaming && x.Role == MessageRole.Assistant);
        p.AssistantPlaceholder.Should().BeSameAs(m[0]);
    }

    [Fact]
    public void Process_Content_StreamingOff_ShouldFillOnCompleteTurn()
    {
        var (p, _) = Create();
        p.Process(ChatStreamEvent.Text("你好"), streamingEnabled: false);
        p.Process(new ChatStreamEvent { Type = ChatStreamEventType.Content, Content = "世界" }, false);

        // 关闭流式时正文只在收尾填充
        p.CompleteTurn(streamingEnabled: false);
        p.AssistantPlaceholder.Content.Should().Be("你好世界");
        p.AssistantPlaceholder.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public void Process_Thinking_EmptyAfterTurn_ShouldRemoveBubble()
    {
        var (p, m) = Create();
        p.Process(new ChatStreamEvent { Type = ChatStreamEventType.Thinking, ThinkingContent = "  " }, true);
        m.Should().Contain(mv => mv.Kind == ChatUiMessageKind.Thinking);

        p.CompleteTurn(true);
        m.Should().NotContain(mv => mv.Kind == ChatUiMessageKind.Thinking, "空思考气泡应收尾时移除");
    }

    [Fact]
    public void Process_ToolLifecycle_ShouldOrderCardsBeforeAssistant()
    {
        var (p, m) = Create();
        p.Process(ChatStreamEvent.ToolStart("Bash", "c1", "ls"), true);
        p.Process(ChatStreamEvent.ToolEnd("Bash", "ok", "c1", isError: false), true);
        p.CompleteTurn(true);

        // 过程在前、助手回复在后
        m[m.Count - 1].Should().BeSameAs(p.AssistantPlaceholder);
        m.Should().Contain(mv => mv.Kind == ChatUiMessageKind.ToolCall && mv.ToolName == "Bash");
        m.Should().Contain(mv => mv.Kind == ChatUiMessageKind.ToolResult && mv.ToolResultText == "ok");

        var callIdx = m.IndexOf(m.First(mv => mv.Kind == ChatUiMessageKind.ToolCall));
        var assistantIdx = m.IndexOf(p.AssistantPlaceholder);
        callIdx.Should().BeLessThan(assistantIdx, "工具卡片必须插到助手占位之前");
    }

    [Fact]
    public void Process_CompleteEvents_ShouldAccumulateTotalTokens()
    {
        var (p, _) = Create();
        p.Process(ChatStreamEvent.Done(new TokenUsage(100, 50)), true);
        p.Process(ChatStreamEvent.Done(new TokenUsage(10, 5)), true);
        p.TotalTokens.Should().Be(165);
    }

    [Fact]
    public void Process_SubAgentEvents_ShouldRouteToSingleGroupCard_NotMainText()
    {
        var (p, m) = Create();
        p.Process(ChatStreamEvent.AgentStarted("a1", "explore", "调研", "executor"), true);
        p.Process(new ChatStreamEvent { Type = ChatStreamEventType.Content, Content = "子代理正文", AgentId = "a1" }, true);
        p.Process(ChatStreamEvent.AgentFinished("a1", success: true), true);
        p.CompleteTurn(true);

        m.Count(mv => mv.Kind == ChatUiMessageKind.AgentRunGroup).Should().Be(1, "整回合只建一张组卡片");
        p.AssistantPlaceholder.Content.Should().BeEmpty("子代理事件不得污染主对话正文");
        p.AgentRuns.Should().ContainSingle(r => r.AgentId == "a1");
    }
}
