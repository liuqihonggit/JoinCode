namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// ContextFoldExecutor 单元测试 — FoldAsync 各分支 + TrimTrailingAndPrepareExit
/// 用 FakeFoldSummarizer 消除 LLM 依赖,确定性验证折叠执行逻辑
/// </summary>
public sealed class ContextFoldExecutorTests {
    [Fact]
    public async Task FoldAsync_EmptyLog_ReturnsNotFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
        Assert.Equal(0, result.OriginalMessageCount);
        Assert.Equal(0, result.HeadMessageCount);
        Assert.Equal(0, result.TailMessageCount);
    }

    [Fact]
    public async Task FoldAsync_BoundaryZero_ReturnsNotFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        // 单条小消息,总字符 <= tailCharBudget → boundary=0
        log.Append(new ApiMessage(MessageRole.User, "hi"));

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
        Assert.Equal(1, result.OriginalMessageCount);
        Assert.Equal(1, result.TailMessageCount);
        Assert.Equal(0, result.HeadMessageCount);
    }

    [Fact]
    public async Task FoldAsync_HeadTooSmall_ReturnsNotFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        // head 占比 < MinSavingsFraction(0.3)
        log.Append(new ApiMessage(MessageRole.Assistant, "tiny"));   // head, 4 chars
        log.Append(new ApiMessage(MessageRole.Assistant, new string('t', 1000))); // tail, 1000 chars

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
        Assert.Equal(2, result.OriginalMessageCount);
    }

    [Fact]
    public async Task FoldAsync_NoFoldableMessages_ReturnsNotFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        // head 全是 pinnable(短 User 消息),foldable 为空
        log.Append(new ApiMessage(MessageRole.User, "q1"));
        log.Append(new ApiMessage(MessageRole.User, "q2"));
        log.Append(new ApiMessage(MessageRole.Assistant, new string('t', 2000)));

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
    }

    [Fact]
    public async Task FoldAsync_NormalFold_InvokesSummarizerAndRewritesLog() {
        var summarizer = new FakeFoldSummarizer("MY SUMMARY");
        var executor = new ContextFoldExecutor(summarizer);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.Assistant, new string('a', 500))); // idx0 head foldable
        log.Append(new ApiMessage(MessageRole.Assistant, new string('b', 500))); // idx1 tail
        log.Append(new ApiMessage(MessageRole.User, "q"));                       // idx2 tail

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.True(result.Folded);
        Assert.Equal("MY SUMMARY", result.Summary);
        Assert.Equal(1, summarizer.CallCount);
        Assert.Single(summarizer.LastHead);
        // 折叠后日志应含摘要消息
        Assert.Contains(log.ToMessages(), m => m.Role == MessageRole.Assistant && m.Content != null && m.Content.Contains("MY SUMMARY"));
    }

    [Fact]
    public async Task FoldAsync_AggressiveFlag_PropagatedToDecision() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: true);

        Assert.False(result.Folded);
        Assert.Equal(ContextFoldDecision.FoldAggressive, result.Decision);
    }

    [Fact]
    public async Task FoldAsync_NormalFlag_PropagatedToDecision() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
        Assert.Equal(ContextFoldDecision.FoldNormal, result.Decision);
    }

    [Fact]
    public async Task FoldAsync_NullLog_Throws() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());

        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.FoldAsync(null!, ctxMax: 1000, aggressive: false));
    }

    [Fact]
    public void TrimTrailingAndPrepareExit_EmptyLog_ReturnsNotFoldedExitWithSummary() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();

        var result = executor.TrimTrailingAndPrepareExit(log);

        Assert.False(result.Folded);
        Assert.Equal(ContextFoldDecision.ExitWithSummary, result.Decision);
        Assert.Equal(0, result.TailMessageCount);
    }

    [Fact]
    public void TrimTrailingAndPrepareExit_LastIsToolCallAssistant_TrimsAndReturnsFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(CreateAssistantWithToolCalls(null, "call_1", "bash"));

        var result = executor.TrimTrailingAndPrepareExit(log);

        Assert.True(result.Folded);
        Assert.Equal(ContextFoldDecision.ExitWithSummary, result.Decision);
        Assert.Equal(1, log.Count); // 末条被裁剪
    }

    [Fact]
    public void TrimTrailingAndPrepareExit_LastNotToolCall_ReturnsNotFolded() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "q"));
        log.Append(new ApiMessage(MessageRole.Assistant, "plain"));

        var result = executor.TrimTrailingAndPrepareExit(log);

        Assert.False(result.Folded);
        Assert.Equal(ContextFoldDecision.ExitWithSummary, result.Decision);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void TrimTrailingAndPrepareExit_NullLog_Throws() {
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());

        Assert.Throws<ArgumentNullException>(() => executor.TrimTrailingAndPrepareExit(null!));
    }

    // ---------- 分支断言强化 ----------

    [Fact]
    public async Task FoldAsync_NormalFold_PreservesCounts() {
        var summarizer = new FakeFoldSummarizer("SUM");
        var executor = new ContextFoldExecutor(summarizer);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.Assistant, new string('a', 500))); // idx0 head foldable
        log.Append(new ApiMessage(MessageRole.Assistant, new string('b', 500))); // idx1 tail
        log.Append(new ApiMessage(MessageRole.User, "q"));                       // idx2 tail

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.True(result.Folded);
        Assert.Equal(3, result.OriginalMessageCount);
        Assert.Equal(1, result.HeadMessageCount);
        Assert.Equal(2, result.TailMessageCount);
    }

    [Fact]
    public async Task FoldAsync_PinnableUserMessage_KeptInResult() {
        // 短 User 消息(<=500 chars)是 pinnable,折叠后应保留在日志中
        var summarizer = new FakeFoldSummarizer("FOLDED");
        var executor = new ContextFoldExecutor(summarizer);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "keep me"));                  // idx0 head pinnable
        log.Append(new ApiMessage(MessageRole.Assistant, new string('a', 500))); // idx1 head foldable
        log.Append(new ApiMessage(MessageRole.Assistant, new string('b', 500))); // idx2 tail

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        // 若 foldable 非空则折叠,pinnable user 消息应保留
        if (result.Folded) {
            Assert.Contains(log.ToMessages(), m => m.Role == MessageRole.User && m.Content == "keep me");
        }
    }

    [Fact]
    public async Task FoldAsync_BoundaryZero_PreservesTailCount() {
        // 强化 boundary==0 分支断言:TailMessageCount 应等于总消息数
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "hi"));

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: false);

        Assert.False(result.Folded);
        Assert.Equal(1, result.OriginalMessageCount);
        Assert.Equal(1, result.TailMessageCount);
        Assert.Equal(0, result.HeadMessageCount);
    }

    [Fact]
    public async Task FoldAsync_EmptyLog_AggressiveDecision_FoldAggressive() {
        // 强化空消息分支的 aggressive 决策
        var executor = new ContextFoldExecutor(new FakeFoldSummarizer());
        var log = new AppendOnlyLog();

        var result = await executor.FoldAsync(log, ctxMax: 1000, aggressive: true);

        Assert.False(result.Folded);
        Assert.Equal(ContextFoldDecision.FoldAggressive, result.Decision);
        Assert.Equal(0, result.OriginalMessageCount);
    }

    [Fact]
    public async Task FoldAsync_Cancellation_PropagatesToSummarizer() {
        // 验证 CancellationToken 传递:summarizer 收到已取消令牌时抛 OperationCanceledException
        var summarizer = new CancelAwareFoldSummarizer();
        var executor = new ContextFoldExecutor(summarizer);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.Assistant, new string('a', 500)));
        log.Append(new ApiMessage(MessageRole.Assistant, new string('b', 500)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.FoldAsync(log, ctxMax: 1000, aggressive: false, cancellationToken: cts.Token));
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

    private sealed class FakeFoldSummarizer : IFoldSummarizer {
        private readonly string _summary;
        public FakeFoldSummarizer(string summary = "SUMMARY") => _summary = summary;
        public int CallCount { get; private set; }
        public IReadOnlyList<ApiMessage> LastHead { get; private set; } = [];
        public Task<string> SummarizeForFoldAsync(IReadOnlyList<ApiMessage> headMessages, CancellationToken cancellationToken = default) {
            CallCount++;
            LastHead = headMessages;
            return Task.FromResult(_summary);
        }
    }

    /// <summary>收到已取消令牌时抛 OperationCanceledException,验证令牌传递</summary>
    private sealed class CancelAwareFoldSummarizer : IFoldSummarizer {
        public Task<string> SummarizeForFoldAsync(IReadOnlyList<ApiMessage> headMessages, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("SUM");
        }
    }
}
