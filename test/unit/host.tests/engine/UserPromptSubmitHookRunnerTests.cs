namespace Host.Tests.Engine;

/// <summary>
/// UserPromptSubmit hook 执行器测试（TASK130-A5）— 该执行器是回落管道（ChatTelemetryPreHook）与
/// 主代理路径（SessionController）共用的唯一实现，所以要在这里锁定三件事：
/// 阻断会中止回合、<c>additionalContext</c> 与 <c>systemMessage</c> 走不同出口、无编排器时 fail-soft。
/// </summary>
public sealed class UserPromptSubmitHookRunnerTests
{
    [Fact]
    public async Task RunAsync_NullOrchestrator_ReturnsEmptyOutcome()
    {
        var outcome = await UserPromptSubmitHookRunner.RunAsync(null, "hi", "s1", CancellationToken.None);

        Assert.False(outcome.Blocked);
        Assert.Null(outcome.AdditionalContext);
        Assert.Empty(outcome.SystemNotices);
    }

    /// <summary>additionalContext 进注入、systemMessage 进旁白 —— 两者不得混用出口。</summary>
    [Fact]
    public async Task RunAsync_SeparatesAdditionalContextFromSystemMessage()
    {
        var orchestrator = new FakeHookOrchestrator(
            new HookResult { Outcome = HookOutcome.Success, AdditionalContext = "上下文注入" },
            new HookResult { Outcome = HookOutcome.Success, SystemMessage = "旁白一" },
            new HookResult { Outcome = HookOutcome.Success, SystemMessage = "旁白二" });

        var outcome = await UserPromptSubmitHookRunner.RunAsync(orchestrator, "hi", "s1", CancellationToken.None);

        Assert.False(outcome.Blocked);
        Assert.Equal("上下文注入", outcome.AdditionalContext);
        Assert.Equal(["旁白一", "旁白二"], outcome.SystemNotices);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsync_BlockingOrPreventContinuation_SetsBlocked(bool blocking, bool preventContinuation)
    {
        var orchestrator = new FakeHookOrchestrator(new HookResult {
            Outcome = blocking ? HookOutcome.Blocking : HookOutcome.Success,
            PreventContinuation = preventContinuation,
            SystemMessage = "不应被采纳的旁白",
        });

        var outcome = await UserPromptSubmitHookRunner.RunAsync(orchestrator, "hi", "s1", CancellationToken.None);

        Assert.True(outcome.Blocked);
        Assert.Empty(outcome.SystemNotices);
    }

    /// <summary>空/缺省字段不产生噪音出口。</summary>
    [Fact]
    public async Task RunAsync_EmptyFields_ProduceNoOutput()
    {
        var orchestrator = new FakeHookOrchestrator(
            new HookResult { Outcome = HookOutcome.Success, AdditionalContext = string.Empty, SystemMessage = string.Empty });

        var outcome = await UserPromptSubmitHookRunner.RunAsync(orchestrator, "hi", "s1", CancellationToken.None);

        Assert.False(outcome.Blocked);
        Assert.Null(outcome.AdditionalContext);
        Assert.Empty(outcome.SystemNotices);
    }

    /// <summary>测试替身 — 按给定结果序列返回，记录收到的 hook 事件与载荷供断言。</summary>
    private sealed class FakeHookOrchestrator(params HookResult[] results) : IHookOrchestrator
    {
        public HookEvent? LastEvent { get; private set; }

        public string? LastPrompt { get; private set; }

        public IAsyncEnumerable<HookResult> ExecuteHooksAsync(
            HookInput input, CancellationToken cancellationToken = default) =>
            ExecuteHooksAsync(input.Event, input.Payload, input.Matcher, input.SessionId, cancellationToken);

        public async IAsyncEnumerable<HookResult> ExecuteHooksAsync(
            HookEvent hookEvent,
            Dictionary<string, JsonElement> payload,
            string? matcher = null,
            string? sessionId = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            LastEvent = hookEvent;
            LastPrompt = payload.TryGetValue("prompt", out var prompt) ? prompt.GetString() : null;
            foreach (var result in results) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return result;
                await Task.Yield();
            }
        }

        public void RegisterHookEventHandler(Action<HookExecutionEvent> handler) { }

        public void UnregisterHookEventHandler(Action<HookExecutionEvent> handler) { }
    }
}
