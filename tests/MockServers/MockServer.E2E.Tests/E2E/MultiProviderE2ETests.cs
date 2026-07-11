namespace MockServer.E2E.Tests;

/// <summary>
/// 多供应商 E2E 测试 — 验证 jcc.exe 与不同厂商 MockServer 的兼容性
/// 每个测试记录执行时间，用于组件验证矩阵
/// </summary>
[Trait("Category", "Integration")]
public sealed class MultiProviderE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;

    public MultiProviderE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        _loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    // ============================================================
    // Anthropic E2E 测试
    // ============================================================

    [Fact]
    public async Task Anthropic_SingleTurn_TextOnly_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnTextOnly, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_SingleTurn_WithToolCall_ShouldShowToolExecution()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnWithToolCall, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_NonInteractive_SinglePrompt_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnNonInteractive, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_StreamingResponse_ShouldReceiveChunks()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.StreamingResponse, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_MultiTurn_ThreeRounds_ShouldMaintainMemory()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.MultiTurnMemory, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ThinkingThenResponse_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ThinkingThenResponse, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(MultiTurnScripts.ToolCallThenFollowUp, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ToolCallWithFollowUpText_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ToolCallWithFollowUpText, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    // ============================================================
    // DeepSeek E2E 测试
    // ============================================================

    [Fact]
    public async Task DeepSeek_SingleTurn_TextOnly_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnTextOnly, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_SingleTurn_WithToolCall_ShouldShowToolExecution()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnWithToolCall, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_NonInteractive_SinglePrompt_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnNonInteractive, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_StreamingResponse_ShouldReceiveChunks()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.StreamingResponse, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_MultiTurn_ThreeRounds_ShouldMaintainMemory()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.MultiTurnMemory, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_ToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(MultiTurnScripts.ToolCallThenFollowUp, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_ToolCallWithFollowUpText_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ToolCallWithFollowUpText, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    // ============================================================
    // 组件覆盖测试 — 验证关键系统组件已被实际调用
    // ============================================================

    [Fact]
    public async Task DualModel_ToolCallThenAnalysis_ShouldWork_WithAnthropic()
    {
        await RunScriptWithProviderAsync(DualModelScripts.ToolCallThenAnalysis, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_DirectTextNoPlan_ShouldWork_WithDeepSeek()
    {
        await RunScriptWithProviderAsync(DualModelScripts.DirectTextNoPlan, ProviderKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ThreeTurnContextPreservation_ShouldWork_WithAnthropic()
    {
        await RunScriptWithProviderAsync(EventStreamScripts.ThreeTurnContextPreservation, ProviderKind.Anthropic).ConfigureAwait(true);
    }

    // ============================================================
    // 测试辅助方法 — 带计时和日志记录
    // ============================================================

    private async Task RunScriptWithProviderAsync(ConversationScript script, ProviderKind provider)
    {
        var sw = Stopwatch.StartNew();
        var runner = new DualRoleConversationRunner(
            _loggerFactory.CreateLogger<DualRoleConversationRunner>());
        try
        {
            var result = await runner.RunAsync(script, provider).ConfigureAwait(true);
            sw.Stop();

            LogResult(result, provider, sw.Elapsed);

            result.AllPassed.Should().BeTrue(
                $"所有断言应通过。失败: {FormatFailures(result)}");
        }
        finally
        {
            await runner.DisposeAsync().ConfigureAwait(true);
        }
    }

    private void LogResult(ConversationResult result, ProviderKind provider, TimeSpan elapsed)
    {
        var elapsedMs = elapsed.TotalMilliseconds;
        _output.WriteLine($"[{provider}] 脚本: {result.ScriptName}");
        _output.WriteLine($"[{provider}] 轮次数: {result.TurnRecords.Count}");
        _output.WriteLine($"[{provider}] 耗时: {elapsedMs:F1}ms");
        _output.WriteLine($"[{provider}] 断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败");

        foreach (var turn in result.TurnRecords)
        {
            _output.WriteLine($"--- Turn: UserInput=\"{turn.UserInput}\"");
            _output.WriteLine($"    ToolCalls: {turn.ToolCalls.Count}");
            var respPreview = turn.AssistantResponse.Length > 100
                ? turn.AssistantResponse[..100] + "..."
                : turn.AssistantResponse;
            _output.WriteLine($"    AssistantResponse: {respPreview}");
            _output.WriteLine($"    Errors: {turn.Errors.Count}");
        }

        foreach (var assert in result.AssertResults.Where(a => !a.IsPassed))
        {
            _output.WriteLine($"FAIL: {assert.Type} Expected=\"{assert.Expected}\" Desc=\"{assert.Description}\"");
        }
    }

    private static string FormatFailures(ConversationResult result)
    {
        var failures = result.AssertResults.Where(a => !a.IsPassed).ToList();
        if (failures.Count == 0) return "(无)";
        return string.Join("; ", failures.Select(f =>
            $"{f.Type}: Expected=\"{f.Expected}\" Desc=\"{f.Description}\""));
    }
}