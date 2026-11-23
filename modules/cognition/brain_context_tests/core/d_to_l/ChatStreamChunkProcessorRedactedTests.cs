namespace Core.Context;

/// <summary>
/// TASK069 #18 — 脱敏思考块处理：AnthropicQueryService 在 redacted_thinking 内容块上打
/// thinking_redacted metadata；处理器据此发带 IsRedacted 标记的 Thinking 事件（无 CoT 文本），
/// 不写入 ThinkingResponse（存根语义，不参与 CoT 累积）。
/// </summary>
public sealed class ChatStreamChunkProcessorRedactedTests {
    private static StreamEvent RedactedChunk() => new() {
        Role = MessageRole.Assistant,
        Content = string.Empty,
        ModelId = "claude",
        Metadata = new Dictionary<string, System.Text.Json.JsonElement> {
            ["Id"] = System.Text.Json.JsonSerializer.SerializeToElement("msg-1"),
            ["thinking_content"] = System.Text.Json.JsonSerializer.SerializeToElement(true),
            ["thinking_redacted"] = System.Text.Json.JsonSerializer.SerializeToElement(true),
        },
    };

    [Fact]
    public async Task ProcessChunk_RedactedThinking_EmitsRedactedThinkingEvent() {
        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        await using var processor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);
        var state = new IterationState();

        var result = processor.ProcessChunk(RedactedChunk(), state);

        result.Action.Should().Be(ChunkAction.Continue);
        result.Events.Should().ContainSingle();
        var evt = result.Events[0];
        evt.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.IsRedacted.Should().BeTrue();
        evt.ThinkingContent.Should().BeEmpty();
        // 脱敏块不进 CoT 累积
        state.ThinkingResponse.Length.Should().Be(0);
    }

    [Fact]
    public async Task ProcessChunk_PlainThinking_NotMarkedRedacted() {
        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        await using var processor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);
        var state = new IterationState();
        var chunk = new StreamEvent {
            Role = MessageRole.Assistant,
            Content = "think",
            ModelId = "claude",
            Metadata = new Dictionary<string, System.Text.Json.JsonElement> {
                ["thinking_content"] = System.Text.Json.JsonSerializer.SerializeToElement(true),
            },
        };

        var result = processor.ProcessChunk(chunk, state);

        result.Events[0].IsRedacted.Should().BeFalse();
        result.Events[0].ThinkingContent.Should().Be("think");
    }
}
