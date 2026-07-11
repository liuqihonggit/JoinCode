namespace MockServer.Core.Tests;

/// <summary>
/// ScriptedResponseStrategyBase.GetContentChunks 流式分片空格保留测试
///
/// 背景: 流式响应将文本分片后逐片发送，客户端拼接还原。
/// 旧实现使用 text.Split(' ') 丢弃空格分隔符，导致拼接后空格丢失
/// （如 "I have read" → "Ihaveread"）。
///
/// 修复目标: 分片本身应携带前导空格，拼接后与原文本完全一致。
/// 参照 IResponseStrategy.GetContentChunks 默认实现的模式:
///   ["Hello", "!", " This", " is", ...]  — 后续分片携带前导空格。
/// </summary>
public sealed class ScriptedResponseStrategyBaseTests
{
    /// <summary>
    /// 测试用派生策略 — 暴露 protected static GetContentChunks(string) 供测试调用
    /// </summary>
    private sealed class TestableScriptedStrategy : ScriptedResponseStrategyBase
    {
        public TestableScriptedStrategy(string defaultResponse)
            : base(turns: null, defaultResponse) { }

        public static string[] InvokeGetContentChunks(string text)
            => GetContentChunks(text);

        // 以下抽象成员提供最小实现，仅供实例化
        public override string BuildResponse(JsonElement request, CacheStats cacheStats) => "{}";
        public override string BuildStreamChunk(string id, string content, bool isLast) => "";
        public override string? BuildStreamPreamble(string id) => null;
        public override string BuildToolCallResponse(JsonElement request, CacheStats cacheStats) => "{}";
        public override string BuildStreamToolCallResponse(string id) => "";
        public override string BuildStreamThinkingResponse(string id) => "";
    }

    [Fact]
    public void GetContentChunks_PreservesSpacesBetweenWords_WhenJoinedBack()
    {
        // 复现 bug: 旧实现 Split(' ') 会丢失空格，拼接得到 "Ihavereadthefile."
        var text = "I have read the file.";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
    }

    [Fact]
    public void GetContentChunks_PreservesMultipleConsecutiveSpaces()
    {
        // 两个连续空格必须完整保留
        var text = "a  b";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
    }

    [Fact]
    public void GetContentChunks_PreservesLeadingSpace()
    {
        var text = " hello";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
    }

    [Fact]
    public void GetContentChunks_PreservesTrailingSpace()
    {
        var text = "hello ";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
    }

    [Fact]
    public void GetContentChunks_EmptyString_ReturnsNonEmptyPlaceholder()
    {
        // 空字符串应返回占位分片，避免流式响应完全无内容
        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(string.Empty);

        chunks.Should().NotBeEmpty();
        string.Concat(chunks).Should().Be(" ");
    }

    [Fact]
    public void GetContentChunks_SingleWord_NoSpaces()
    {
        var text = "Hello!";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
    }

    [Fact]
    public void GetContentChunks_RealWorldScenario_StreamingResponseText()
    {
        // 模拟真实 MockServer 响应文本 — 对应 E2E 测试中观察到的空格丢失场景
        var text = "I have read the file. The task is complete.";

        var chunks = TestableScriptedStrategy.InvokeGetContentChunks(text);
        var rejoined = string.Concat(chunks);

        rejoined.Should().Be(text);
        // 验证没有产生空字符串分片（空分片会导致无意义的网络包）
        chunks.Should().NotContain(string.Empty);
    }
}
