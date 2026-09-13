namespace Integration.Tests.PrefixCache.Unit;

/// <summary>
/// 验证客户端对"消息序列"的缓存前缀检测 —— 对齐线上真实字节前缀语义。
///
/// 背景: 原 CacheBreakDetector 只对 system/tools/dynamic 三项逻辑构件做 hash,
/// 从不检测对话消息序列。若中途某条历史消息被篡改/插入, 真实线上前缀已被破坏,
/// 但检测器误报"无失效"。本组测试要求把对话序列纳入检测:
/// - 多轮纯增长 (在尾部追加) → 前缀稳定, 不误报
/// - 中途某条已存在消息被篡改 → 必须报 ConversationHistoryChanged
/// - 撤回/截断 (前缀变短) → 新前缀是已缓存前缀的前缀 → 仍可命中, 不误报
/// </summary>
public sealed class CacheBreakDetectorConversationHistoryTests
{
    private readonly CacheBreakDetector _detector = new();

    private static TokenUsage HitUsage() => new(200, 50)
    {
        CacheReadInputTokens = 180,
        CacheCreationInputTokens = 0
    };

    [Fact]
    public void MultiTurnGrowth_AppendTailOnly_ShouldNotBreak()
    {
        var prefix = new ImmutablePrefix("System", [], []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic",
            conversation:
            [
                new ApiMessage(MessageRole.User, "u1")
            ]);

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1"),
                new ApiMessage(MessageRole.User, "u2")
            ]);

        result.BreakDetected.Should().BeFalse(
            "denormalized: only previously-seen messages changed (tail appended) keeps wire prefix stable");
    }

    [Fact]
    public void TamperExistingAssistantMessage_ShouldBreak()
    {
        var prefix = new ImmutablePrefix("System", new List<ToolSpec>(), []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic",
            conversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "A1-CHANGED")
            ]);

        result.BreakDetected.Should().BeTrue(
            "a mid-history message change invalidates the real wire prefix but the old detector never checked it");
        result.Kind.Should().Be(CacheBreakKind.ConversationHistoryChanged);
    }

    [Fact]
    public void TamperFirstMessage_ShouldBreak()
    {
        var prefix = new ImmutablePrefix("System", new List<ToolSpec>(), []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic",
            conversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "CHANGED"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ConversationHistoryChanged);
    }

    [Fact]
    public void InsertMessageInMiddle_ShouldBreak()
    {
        var prefix = new ImmutablePrefix("System", new List<ToolSpec>(), []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic",
            conversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Tool, "injected"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        result.BreakDetected.Should().BeTrue(
            "insertion shifts the tail of the prefix, invalidating the wire cache");
        result.Kind.Should().Be(CacheBreakKind.ConversationHistoryChanged);
    }

    [Fact]
    public void RewindShrink_ShorterPrefix_ShouldNotBreak()
    {
        var prefix = new ImmutablePrefix("System", new List<ToolSpec>(), []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic",
            conversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1"),
                new ApiMessage(MessageRole.User, "u2")
            ]);

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "u1"),
                new ApiMessage(MessageRole.Assistant, "a1")
            ]);

        result.BreakDetected.Should().BeFalse(
            "shrunk prefix is a prefix of the previously cached one, so cache remains hit-able");
    }

    [Fact]
    public void SnapshotWasEmptyConversation_ShouldNotBreak()
    {
        var prefix = new ImmutablePrefix("System", new List<ToolSpec>(), []);
        var snapshot = _detector.RecordPromptState(prefix, "dynamic");

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", HitUsage(),
            currentConversation:
            [
                new ApiMessage(MessageRole.User, "u1")
            ]);

        result.BreakDetected.Should().BeFalse(
            "when no conversation existed at snapshot time, history detection is skipped for backward compatibility");
    }
}
