namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldSnipTests
{
    private const int CtxMax = 4000;

    private static string BigResult(int length)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
            sb.Append((char)('a' + i % 26));
        return sb.ToString();
    }

    private static AppendOnlyLog LogWithStaleToolResult(int resultLength)
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "turn 1"));
        log.Append(new ApiMessage(MessageRole.Assistant, "tool call",
            new Dictionary<string, JsonElement> { ["ToolCalls"] = JsonSerializer.SerializeToElement("[]") }));
        log.Append(new ApiMessage(MessageRole.Tool, BigResult(resultLength),
            new Dictionary<string, JsonElement> { ["ToolName"] = JsonSerializer.SerializeToElement("bash") }));
        log.Append(new ApiMessage(MessageRole.User, "recent follow-up"));
        return log;
    }

    [Fact]
    public void Snip_StaleLargeToolResult_RewritesToPlaceholder()
    {
        var log = LogWithStaleToolResult(5000);

        var stats = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        stats.Results.Should().Be(1);
        stats.SavedChars.Should().BeGreaterThan(0);
        log[2].Content!.Length.Should().BeLessThan(BigResult(5000).Length, "snipped content must be strictly shorter");
        log[2].Content.Should().Contain("snipped");
        log[2].Content.Should().Contain("bash");
        log.Count.Should().Be(4, "snip must not drop messages");
    }

    [Fact]
    public void Snip_SkipsSmallToolResults()
    {
        var log = LogWithStaleToolResult(200);

        var stats = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        stats.Results.Should().Be(0);
        log[2].Content.Should().Be(BigResult(200));
    }

    [Fact]
    public void Snip_KeepsProtectedTailVerbatim()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "old turn"));
        log.Append(new ApiMessage(MessageRole.Assistant, "old tool call",
            new Dictionary<string, JsonElement> { ["ToolCalls"] = JsonSerializer.SerializeToElement("[]") }));
        log.Append(new ApiMessage(MessageRole.Tool, BigResult(5000),
            new Dictionary<string, JsonElement> { ["ToolName"] = JsonSerializer.SerializeToElement("bash") }));
        log.Append(new ApiMessage(MessageRole.User, "recent turn"));
        log.Append(new ApiMessage(MessageRole.Assistant, "recent reply"));
        log.Append(new ApiMessage(MessageRole.Tool, BigResult(2000),
            new Dictionary<string, JsonElement> { ["ToolName"] = JsonSerializer.SerializeToElement("recent_tool") }));

        var stats = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        stats.Results.Should().Be(1, "only the stale tool result older than the protected tail must be snipped");
        log[5].Content.Should().Be(BigResult(2000), "tool result in the protected tail must stay verbatim");
        log[2].Content.Should().Contain("snipped");
    }

    [Fact]
    public void Snip_IsIdempotent()
    {
        var log = LogWithStaleToolResult(5000);

        var first = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);
        first.Results.Should().Be(1);

        var second = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);
        second.Results.Should().Be(0, "already-snipped results must not be re-snipped");
    }

    [Fact]
    public void Snip_KeepsToolCallPairingMetadata()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "turn 1"));
        log.Append(new ApiMessage(MessageRole.Assistant, "tool call",
            new Dictionary<string, JsonElement>
            {
                ["ToolCalls"] = JsonSerializer.SerializeToElement("[{\"Id\":\"call_1\",\"Name\":\"bash\"}]")
            }));
        var toolMsg = new ApiMessage(MessageRole.Tool, BigResult(5000),
            new Dictionary<string, JsonElement>
            {
                ["ToolName"] = JsonSerializer.SerializeToElement("bash"),
                ["ToolCallId"] = JsonSerializer.SerializeToElement("call_1")
            });
        log.Append(toolMsg);
        log.Append(new ApiMessage(MessageRole.User, "recent follow-up"));

        ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        log[2].ExtractToolCallId().Should().Be("call_1");
        log[2].ExtractToolName().Should().Be("bash");
    }

    [Fact]
    public void Snip_LineBranch_KeepsHeadAndTailLines()
    {
        var lines = Enumerable.Range(0, 200)
            .Select(i => $"LINE_{i}_" + new string('x', 40));
        var content = string.Join("\n", lines);
        var log = BuildToolResult(content);

        var stats = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        stats.Results.Should().Be(1);
        stats.SavedChars.Should().BeGreaterThan(0);
        log[2].Content.Should().Contain("LINE_0_");
        log[2].Content.Should().Contain("LINE_199_");
        log[2].Content.Should().Contain("[... 120 lines omitted ...]");
    }

    [Fact]
    public void Snip_SkipsWhenRewriteIsNotShorter()
    {
        // 81 行×~40 字符：只略超 40+40 行阈值，保留 80 行 + 2 个 marker 反而比原文更长。
        // 剪裁必须承诺严格变短，否则跳过（避免上下文膨胀与负 SavedChars）。
        var lines = Enumerable.Repeat(new string('b', 40), 81);
        var log = BuildToolResult(string.Join("\n", lines));

        var stats = ContextFoldDecider.SnipStaleToolResults(log, CtxMax);

        stats.Results.Should().Be(0, "重写不变短时必须跳过");
        stats.SavedChars.Should().Be(0);
        log[2].Content.Should().HaveLength(81 * 40 + 80);
    }

    private static AppendOnlyLog BuildToolResult(string content)
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "turn 1"));
        log.Append(new ApiMessage(MessageRole.Assistant, "tool call",
            new Dictionary<string, JsonElement> { ["ToolCalls"] = JsonSerializer.SerializeToElement("[]") }));
        log.Append(new ApiMessage(MessageRole.Tool, content,
            new Dictionary<string, JsonElement> { ["ToolName"] = JsonSerializer.SerializeToElement("bash") }));
        log.Append(new ApiMessage(MessageRole.User, "recent follow-up"));
        return log;
    }
}
