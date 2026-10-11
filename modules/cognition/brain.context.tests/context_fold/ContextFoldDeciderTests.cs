// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC1017, JCC11003
namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// ContextFoldDecider 单元测试 — 上下文折叠决策器 8 个纯函数方法
/// 覆盖各阈值边界、延迟折叠、alreadyFolded、保护区边界、token 估算、卡死判定、末尾裁剪、过期剪裁
/// </summary>
public sealed class ContextFoldDeciderTests {
    // ---------- DecideAfterUsage ----------

    [Fact]
    public void DecideAfterUsage_RatioAboveForceSummary_ReturnsExitWithSummary() {
        var usage = new TokenUsage(900, 0);

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false);

        Assert.Equal(ContextFoldDecision.ExitWithSummary, decision);
    }

    [Fact]
    public void DecideAfterUsage_ForceSummaryBeatsAlreadyFolded() {
        var usage = new TokenUsage(900, 0);

        // ratio=0.9 > ForceSummary(0.8),即使 alreadyFolded 也应 ExitWithSummary
        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: true);

        Assert.Equal(ContextFoldDecision.ExitWithSummary, decision);
    }

    [Fact]
    public void DecideAfterUsage_AggressiveRatio_ReturnsFoldAggressive() {
        var usage = new TokenUsage(750, 0);

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false);

        Assert.Equal(ContextFoldDecision.FoldAggressive, decision);
    }

    [Fact]
    public void DecideAfterUsage_NormalRatio_ReturnsFoldNormal() {
        var usage = new TokenUsage(600, 0);

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false);

        Assert.Equal(ContextFoldDecision.FoldNormal, decision);
    }

    [Fact]
    public void DecideAfterUsage_LowRatio_ReturnsNone() {
        var usage = new TokenUsage(400, 0);

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false);

        Assert.Equal(ContextFoldDecision.None, decision);
    }

    [Fact]
    public void DecideAfterUsage_AlreadyFoldedAndNotForceSummary_ReturnsNone() {
        var usage = new TokenUsage(750, 0);

        // ratio=0.75 > Aggressive(0.7) 但 alreadyFolded,应 None
        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: true);

        Assert.Equal(ContextFoldDecision.None, decision);
    }

    [Fact]
    public void DecideAfterUsage_CacheReadAndDeferralWithinLimit_ReturnsDeferred() {
        var usage = new TokenUsage(600, 0) { CacheReadInputTokens = 100 };

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false, deferralCount: 1);

        Assert.Equal(ContextFoldDecision.Deferred, decision);
    }

    [Fact]
    public void DecideAfterUsage_CacheReadButDeferralAtLimit_ReturnsFoldNormal() {
        var usage = new TokenUsage(600, 0) { CacheReadInputTokens = 100 };

        // deferralCount=3 == DeferFoldLimit(3),不再延迟
        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false, deferralCount: 3);

        Assert.Equal(ContextFoldDecision.FoldNormal, decision);
    }

    [Fact]
    public void DecideAfterUsage_NoCacheRead_ReturnsFoldNormalNotDeferred() {
        var usage = new TokenUsage(600, 0);

        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false);

        Assert.Equal(ContextFoldDecision.FoldNormal, decision);
    }

    [Fact]
    public void DecideAfterUsage_NullUsage_Throws() {
        Assert.Throws<ArgumentNullException>(() =>
            ContextFoldDecider.DecideAfterUsage(null!, ctxMax: 1000, alreadyFoldedThisTurn: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DecideAfterUsage_NonPositiveCtxMax_Throws(int ctxMax) {
        var usage = new TokenUsage(100, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextFoldDecider.DecideAfterUsage(usage, ctxMax, alreadyFoldedThisTurn: false));
    }

    [Fact]
    public void DecideAfterUsage_CustomThresholds_Applied() {
        var usage = new TokenUsage(600, 0);
        var thresholds = new ContextFoldThresholds { FoldThreshold = 0.4, AggressiveThreshold = 0.9, ForceSummaryThreshold = 0.95 };

        // ratio=0.6 > FoldThreshold(0.4) 但 < Aggressive(0.9) → FoldNormal
        var decision = ContextFoldDecider.DecideAfterUsage(usage, ctxMax: 1000, alreadyFoldedThisTurn: false, thresholds);

        Assert.Equal(ContextFoldDecision.FoldNormal, decision);
    }

    // ---------- DecidePreflight ----------

    [Fact]
    public void DecidePreflight_EmptyMessagesAndTools_ReturnsNoAction() {
        var decision = ContextFoldDecider.DecidePreflight([], [], ctxMax: 1000);

        Assert.False(decision.NeedsAction);
        Assert.Equal(0, decision.EstimatedRatio);
    }

    [Fact]
    public void DecidePreflight_HugeContent_ReturnsNeedsAction() {
        // 8000 chars / 4 = 2000 tokens, ratio = 2000/1000 = 2.0 > Emergency(0.95)
        var messages = new List<ApiMessage> { new(MessageRole.User, new string('x', 8000)) };

        var decision = ContextFoldDecider.DecidePreflight(messages, [], ctxMax: 1000);

        Assert.True(decision.NeedsAction);
        Assert.True(decision.EstimatedRatio > 0.95);
    }

    [Fact]
    public void DecidePreflight_SmallContent_ReturnsNoAction() {
        var messages = new List<ApiMessage> { new(MessageRole.User, "hi") };

        var decision = ContextFoldDecider.DecidePreflight(messages, [], ctxMax: 1000);

        Assert.False(decision.NeedsAction);
    }

    [Fact]
    public void DecidePreflight_NullMessages_Throws() {
        Assert.Throws<ArgumentNullException>(() =>
            ContextFoldDecider.DecidePreflight(null!, [], ctxMax: 1000));
    }

    [Fact]
    public void DecidePreflight_NonPositiveCtxMax_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextFoldDecider.DecidePreflight([], [], ctxMax: 0));
    }

    // ---------- EstimateTokenCount ----------

    [Fact]
    public void EstimateTokenCount_Empty_ReturnsZero() {
        Assert.Equal(0, ContextFoldDecider.EstimateTokenCount([], []));
    }

    [Fact]
    public void EstimateTokenCount_MessageContent_DividedByCharsPerToken() {
        var messages = new List<ApiMessage> { new(MessageRole.User, "12345678") }; // 8 chars / 4 = 2

        Assert.Equal(2, ContextFoldDecider.EstimateTokenCount(messages, []));
    }

    [Fact]
    public void EstimateTokenCount_NullContent_Skipped() {
        var messages = new List<ApiMessage> { new(MessageRole.User, null) };

        Assert.Equal(0, ContextFoldDecider.EstimateTokenCount(messages, []));
    }

    [Fact]
    public void EstimateTokenCount_ToolSpecAllFields() {
        var specs = new List<ToolSpec> { new("name", "desc", "schema") }; // 4+4+6=14 / 4 = 3

        Assert.Equal(3, ContextFoldDecider.EstimateTokenCount([], specs));
    }

    [Fact]
    public void EstimateTokenCount_ToolSpecNullDescriptionAndSchema() {
        var specs = new List<ToolSpec> { new("name") }; // 4 / 4 = 1

        Assert.Equal(1, ContextFoldDecider.EstimateTokenCount([], specs));
    }

    [Fact]
    public void EstimateTokenCount_CustomCharsPerToken() {
        var thresholds = new ContextFoldThresholds { CharsPerToken = 2 };
        var messages = new List<ApiMessage> { new(MessageRole.User, "1234") }; // 4 / 2 = 2

        Assert.Equal(2, ContextFoldDecider.EstimateTokenCount(messages, [], thresholds));
    }

    // ---------- ComputeTailBoundary ----------

    [Fact]
    public void ComputeTailBoundary_EmptyMessages_ReturnsZero() {
        Assert.Equal(0, ContextFoldDecider.ComputeTailBoundary([], ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void ComputeTailBoundary_SingleSmallMessage_ReturnsZero() {
        var messages = new List<ApiMessage> { new(MessageRole.Assistant, "hi") };

        // 总字符 2 <= tailCharBudget(1000*0.2*4=800),boundary=Count=1→0
        Assert.Equal(0, ContextFoldDecider.ComputeTailBoundary(messages, ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void ComputeTailBoundary_SingleMessageExceedsBudget_ReturnsZeroByDesign() {
        // 末条单独超预算时,boundary=Count 触发重置为 0(整个日志视作保护区)
        // 这是设计兜底:SnipStaleToolResults 注释明确说明此行为
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('a', 900)),
        };

        Assert.Equal(0, ContextFoldDecider.ComputeTailBoundary(messages, ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void ComputeTailBoundary_LeadingMessageExceedsBudget_BreaksBeforeTail() {
        // 前条超预算,后条小:从末尾累加后条不 break,前条累加超预算 break → boundary=前条索引+1
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('a', 900)), // idx 0
            new(MessageRole.Assistant, "ok"),                 // idx 1
        };

        Assert.Equal(1, ContextFoldDecider.ComputeTailBoundary(messages, ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void ComputeTailBoundary_UserMessage_AlignsBoundaryToUserIndex() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('a', 100)), // idx 0
            new(MessageRole.User, "q"),                       // idx 1, boundary=1
            new(MessageRole.Assistant, "ok"),                 // idx 2
        };

        // 从末尾累加:ok(2) charCount=2, User(1) boundary=1 charCount=3, Assistant(100) 3+100>800 break boundary=0+1=1
        Assert.Equal(1, ContextFoldDecider.ComputeTailBoundary(messages, ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void ComputeTailBoundary_Aggressive_UsesSmallerFraction() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('a', 50)),  // idx 0
            new(MessageRole.Assistant, new string('b', 50)),  // idx 1
        };

        // aggressive: tailFraction=0.1, tailCharBudget=1000*0.1*4=400。总 100 <= 400,boundary=Count=2→0
        Assert.Equal(0, ContextFoldDecider.ComputeTailBoundary(messages, ctxMax: 1000, aggressive: true));
    }

    [Fact]
    public void ComputeTailBoundary_NonPositiveCtxMax_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextFoldDecider.ComputeTailBoundary([], ctxMax: 0, aggressive: false));
    }

    // ---------- ShouldFold ----------

    [Fact]
    public void ShouldFold_EmptyMessages_ReturnsFalse() {
        Assert.False(ContextFoldDecider.ShouldFold([], headStart: 0, headEnd: 0, ctxMax: 1000));
    }

    [Fact]
    public void ShouldFold_AllNullContent_ReturnsFalse() {
        var messages = new List<ApiMessage> { new(MessageRole.User, null), new(MessageRole.Assistant, null) };

        Assert.False(ContextFoldDecider.ShouldFold(messages, headStart: 0, headEnd: 2, ctxMax: 1000));
    }

    [Fact]
    public void ShouldFold_HeadFractionAboveMinSavings_ReturnsTrue() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('h', 60)), // head
            new(MessageRole.Assistant, new string('t', 40)), // tail
        };

        // headFraction = 60/100 = 0.6 >= MinSavingsFraction(0.3)
        Assert.True(ContextFoldDecider.ShouldFold(messages, headStart: 0, headEnd: 1, ctxMax: 1000));
    }

    [Fact]
    public void ShouldFold_HeadFractionBelowMinSavings_ReturnsFalse() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('h', 10)), // head
            new(MessageRole.Assistant, new string('t', 90)), // tail
        };

        // headFraction = 10/100 = 0.1 < 0.3
        Assert.False(ContextFoldDecider.ShouldFold(messages, headStart: 0, headEnd: 1, ctxMax: 1000));
    }

    [Fact]
    public void ShouldFold_CustomMinSavingsFraction() {
        var thresholds = new ContextFoldThresholds { MinSavingsFraction = 0.5 };
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, new string('h', 40)),
            new(MessageRole.Assistant, new string('t', 60)),
        };

        // headFraction = 0.4 < 0.5
        Assert.False(ContextFoldDecider.ShouldFold(messages, headStart: 0, headEnd: 1, ctxMax: 1000, thresholds));
    }

    [Fact]
    public void ShouldFold_NonPositiveCtxMax_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextFoldDecider.ShouldFold([], 0, 0, ctxMax: 0));
    }

    // ---------- IsFoldStuck ----------

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    [InlineData(2, 3, false)]
    [InlineData(0, 3, false)]
    public void IsFoldStuck_BoundaryCases(int consecutive, int limit, bool expected) {
        Assert.Equal(expected, ContextFoldDecider.IsFoldStuck(consecutive, limit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsFoldStuck_NonPositiveLimit_Throws(int limit) {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextFoldDecider.IsFoldStuck(1, limit));
    }

    // ---------- TrimTrailingToolCalls ----------

    [Fact]
    public void TrimTrailingToolCalls_EmptyLog_ReturnsFalse() {
        var log = new AppendOnlyLog();

        Assert.False(ContextFoldDecider.TrimTrailingToolCalls(log));
    }

    [Fact]
    public void TrimTrailingToolCalls_LastNotAssistant_ReturnsFalse() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "hi"));

        Assert.False(ContextFoldDecider.TrimTrailingToolCalls(log));
    }

    [Fact]
    public void TrimTrailingToolCalls_LastAssistantWithoutToolCalls_ReturnsFalse() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.Assistant, "plain text"));

        Assert.False(ContextFoldDecider.TrimTrailingToolCalls(log));
    }

    [Fact]
    public void TrimTrailingToolCalls_LastAssistantWithToolCallsNoContent_RemovesLast() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(CreateAssistantWithToolCalls(content: null, "call_1", "bash"));

        var trimmed = ContextFoldDecider.TrimTrailingToolCalls(log);

        Assert.True(trimmed);
        Assert.Equal(1, log.Count);
        Assert.Equal(MessageRole.User, log[0].Role);
    }

    [Fact]
    public void TrimTrailingToolCalls_LastAssistantWithToolCallsAndContent_KeepsTextContent() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(CreateAssistantWithToolCalls(content: "explanation text", "call_1", "bash"));

        var trimmed = ContextFoldDecider.TrimTrailingToolCalls(log);

        Assert.True(trimmed);
        Assert.Equal(2, log.Count);
        Assert.Equal(MessageRole.Assistant, log[1].Role);
        Assert.Equal("explanation text", log[1].Content);
        // 保留 content 的消息不应再带 ToolCalls metadata
        Assert.False(log[1].Metadata.ContainsKey("ToolCalls"));
    }

    [Fact]
    public void TrimTrailingToolCalls_NullLog_Throws() {
        Assert.Throws<ArgumentNullException>(() => ContextFoldDecider.TrimTrailingToolCalls(null!));
    }

    // ---------- SnipStaleToolResults ----------

    [Fact]
    public void SnipStaleToolResults_EmptyLog_ReturnsZeroStats() {
        var log = new AppendOnlyLog();

        var stats = ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);

        Assert.Equal(0, stats.Results);
        Assert.Equal(0, stats.SavedChars);
    }

    [Fact]
    public void SnipStaleToolResults_NoToolMessages_ReturnsZeroStats() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(new ApiMessage(MessageRole.Assistant, "a"));

        var stats = ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);

        Assert.Equal(0, stats.Results);
    }

    [Fact]
    public void SnipStaleToolResults_LargeToolResult_SnippedAndSaved() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(CreateToolResult("call_1", new string('x', 2000)));
        log.Append(new ApiMessage(MessageRole.User, "q2"));
        log.Append(new ApiMessage(MessageRole.Assistant, "a"));

        var stats = ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);

        Assert.Equal(1, stats.Results);
        Assert.True(stats.SavedChars > 0);
        // 工具结果内容应被替换为 snipped 占位符
        var toolMsg = log.ToMessages().First(m => m.Role == MessageRole.Tool);
        Assert.StartsWith("snipped:", toolMsg.Content);
    }

    [Fact]
    public void SnipStaleToolResults_Idempotent_SecondCallNoChange() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(CreateToolResult("call_1", new string('x', 2000)));
        log.Append(new ApiMessage(MessageRole.User, "q2"));
        log.Append(new ApiMessage(MessageRole.Assistant, "a"));

        ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);
        var stats2 = ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);

        Assert.Equal(0, stats2.Results);
        Assert.Equal(0, stats2.SavedChars);
    }

    [Fact]
    public void SnipStaleToolResults_MultimodalContentBlocks_Skipped() {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        // 带 ContentBlocks 的工具结果不剪裁(避免破坏多模态配对)
        var toolMsg = new ApiMessage(MessageRole.Tool, new string('x', 2000)) {
            ContentBlocks = new List<ToolContent> { new() { Type = ToolContentType.Image, Data = "base64" } }
        };
        log.Append(toolMsg);
        log.Append(new ApiMessage(MessageRole.User, "q2"));
        log.Append(new ApiMessage(MessageRole.Assistant, "a"));

        var stats = ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 1000);

        Assert.Equal(0, stats.Results);
    }

    [Fact]
    public void SnipStaleToolResults_NonPositiveCtxMax_Throws() {
        var log = new AppendOnlyLog();
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextFoldDecider.SnipStaleToolResults(log, ctxMax: 0));
    }

    [Fact]
    public void SnipStaleToolResults_NullLog_Throws() {
        Assert.Throws<ArgumentNullException>(() => ContextFoldDecider.SnipStaleToolResults(null!, ctxMax: 1000));
    }

    // ---------- Helpers ----------

    private static ApiMessage CreateAssistantWithToolCalls(string? content, string id, string name) {
        var json = $$"""[{"Id":"{{id}}","Name":"{{name}}","Arguments":"{}"}]""";
        using var doc = JsonDocument.Parse(json);
        var metadata = new Dictionary<string, JsonElement> {
            ["ToolCalls"] = doc.RootElement.Clone(),
        };
        return new ApiMessage(MessageRole.Assistant, content, metadata);
    }

    private static ApiMessage CreateToolResult(string toolCallId, string content) {
        var metadata = new Dictionary<string, JsonElement> {
            ["ToolCallId"] = JsonSerializer.SerializeToElement(toolCallId),
        };
        return new ApiMessage(MessageRole.Tool, content, metadata);
    }
}
