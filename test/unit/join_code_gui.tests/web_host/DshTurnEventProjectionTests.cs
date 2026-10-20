namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 轮次事件 → DSH 助手消息投影测试（TASK058 B4）：
/// 引擎 Thinking 流 → assistant/message 前置 reasoning 块（前端 toAssistantBlock type=reasoning）；
/// Complete.Usage（TokenUsage）→ data.usage 四桶（前端 TurnUsagePanel 消费）。
/// </summary>
public sealed class DshTurnEventProjectionTests {
    [Fact]
    public void ThinkingAndText_ReasoningBlockPrecedesText() {
        var projection = DshAssistantReplyProjection.Project(thinking: "先分析问题", text: "答案是 42", usage: null);

        projection.Content.Should().HaveCount(2);
        projection.Content[0].Type.Should().Be("reasoning");
        projection.Content[0].Text.Should().Be("先分析问题");
        projection.Content[1].Type.Should().Be("text");
        projection.Content[1].Text.Should().Be("答案是 42");
        projection.Usage.Should().BeNull();
    }

    [Fact]
    public void TextOnly_NoReasoningBlock() {
        var projection = DshAssistantReplyProjection.Project(thinking: null, text: "直接回答", usage: null);

        projection.Content.Should().ContainSingle().Which.Type.Should().Be("text");
    }

    [Fact]
    public void Usage_MapsEngineBucketsToFrontendShape() {
        var usage = new TokenUsage(promptTokens: 100, completionTokens: 40) {
            CacheReadInputTokens = 60,
            CacheCreationInputTokens = 10,
            ReasoningTokens = 5,
        };

        var projection = DshAssistantReplyProjection.Project(null, "回复", usage);

        var mapped = projection.Usage.Should().BeOfType<DshTurnUsage>().Subject;
        mapped.OutputTokens.Should().Be(40);
        mapped.CacheReadTokens.Should().Be(60);
        mapped.CacheWriteTokens.Should().Be(10);
        mapped.UncachedInputTokens.Should().Be(40, "prompt(100) - cacheRead(60) = 未缓存输入");
        mapped.TotalTokens.Should().Be(150, "四桶之和 = 40+60+10+40（对齐前端 tokenTotal）");
        mapped.ReasoningTokens.Should().Be(5);
    }

    [Fact]
    public void ThinkingOnly_StillEmitsReasoningBlockWithoutText() {
        // 纯思考轮（模型只输出 reasoning 无正文）也要把思考内容带给前端
        var projection = DshAssistantReplyProjection.Project(thinking: "仅思考", text: "", usage: null);

        projection.Content.Should().ContainSingle().Which.Type.Should().Be("reasoning");
    }
}
