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
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnTextOnly, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_SingleTurn_WithToolCall_ShouldShowToolExecution()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnWithToolCall, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_NonInteractive_SinglePrompt_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnNonInteractive, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_StreamingResponse_ShouldReceiveChunks()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.StreamingResponse, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_MultiTurn_ThreeRounds_ShouldMaintainMemory()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.MultiTurnMemory, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ThinkingThenResponse_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ThinkingThenResponse, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(MultiTurnScripts.ToolCallThenFollowUp, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task Anthropic_ToolCallWithFollowUpText_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ToolCallWithFollowUpText, VendorKind.Anthropic).ConfigureAwait(true);
    }

    // ============================================================
    // DeepSeek E2E 测试
    // ============================================================

    [Fact]
    public async Task DeepSeek_SingleTurn_TextOnly_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnTextOnly, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_SingleTurn_WithToolCall_ShouldShowToolExecution()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnWithToolCall, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_NonInteractive_SinglePrompt_ShouldGetResponse()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.SingleTurnNonInteractive, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_StreamingResponse_ShouldReceiveChunks()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.StreamingResponse, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_MultiTurn_ThreeRounds_ShouldMaintainMemory()
    {
        await RunScriptWithProviderAsync(BasicConversationScripts.MultiTurnMemory, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_ToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(MultiTurnScripts.ToolCallThenFollowUp, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task DeepSeek_ToolCallWithFollowUpText_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(ToolCallScripts.ToolCallWithFollowUpText, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    // ============================================================
    // 组件覆盖测试 — 验证关键系统组件已被实际调用
    // ============================================================

    [Fact]
    public async Task DualModel_ToolCallThenAnalysis_ShouldWork_WithAnthropic()
    {
        await RunScriptWithProviderAsync(DualModelScripts.ToolCallThenAnalysis, VendorKind.Anthropic).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_DirectTextNoPlan_ShouldWork_WithDeepSeek()
    {
        await RunScriptWithProviderAsync(DualModelScripts.DirectTextNoPlan, VendorKind.DeepSeek).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ThreeTurnContextPreservation_ShouldWork_WithAnthropic()
    {
        await RunScriptWithProviderAsync(EventStreamScripts.ThreeTurnContextPreservation, VendorKind.Anthropic).ConfigureAwait(true);
    }

    // ============================================================
    // 测试辅助方法 — 带计时和日志记录
    // ============================================================

    private async Task RunScriptWithProviderAsync(ConversationScript script, VendorKind provider)
    {
        const int maxAttempts = 3;
        var attemptDurations = new List<TimeSpan>();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sw = Stopwatch.StartNew();
            var runner = new DualRoleConversationRunner(
                _loggerFactory.CreateLogger<DualRoleConversationRunner>());
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var result = await runner.RunAsync(script, provider, timeoutCts.Token).ConfigureAwait(true);
                sw.Stop();
                attemptDurations.Add(sw.Elapsed);

                LogResult(result, provider, sw.Elapsed);

                if (result.AllPassed)
                    return;

                if (attempt < maxAttempts)
                {
                    var backoffMs = (int)Math.Pow(2, attempt - 1) * 1000;
                    _output.WriteLine($"[{provider}] ⚠ 第{attempt}次尝试失败(elapsed={sw.Elapsed.TotalMilliseconds:F0}ms)，{backoffMs}ms后重试: {script.Name}");
                    await Task.Delay(backoffMs).ConfigureAwait(true);
                    continue;
                }

                result.AllPassed.Should().BeTrue(
                    $"所有断言应通过。失败: {FormatFailures(result)}");
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                attemptDurations.Add(sw.Elapsed);
                var stderrTail = await CaptureStderrTailAsync(runner).ConfigureAwait(true);
                if (attempt < maxAttempts)
                {
                    var backoffMs = (int)Math.Pow(2, attempt - 1) * 1000;
                    _output.WriteLine($"[{provider}] ⚠ 第{attempt}次尝试超时(>60s, elapsed={sw.Elapsed.TotalMilliseconds:F0}ms)，{backoffMs}ms后重试: {script.Name}");
                    if (stderrTail.Length > 0)
                        _output.WriteLine($"[{provider}] stderr尾部: {stderrTail}");
                    await Task.Delay(backoffMs).ConfigureAwait(true);
                    continue;
                }
                var durationSummary = string.Join(", ", attemptDurations.Select(d => $"{d.TotalMilliseconds:F0}ms"));
                throw new TimeoutException($"[GEN039] 测试超时(>60s): {script.Name} (provider={provider}, attempts={maxAttempts}, durations=[{durationSummary}])");
            }
            catch (TimeoutException ex)
            {
                sw.Stop();
                attemptDurations.Add(sw.Elapsed);
                if (attempt < maxAttempts)
                {
                    var backoffMs = (int)Math.Pow(2, attempt - 1) * 1000;
                    _output.WriteLine($"[{provider}] ⚠ 第{attempt}次尝试超时({ex.Message})，{backoffMs}ms后重试: {script.Name}");
                    await Task.Delay(backoffMs).ConfigureAwait(true);
                    continue;
                }
                var durationSummary = string.Join(", ", attemptDurations.Select(d => $"{d.TotalMilliseconds:F0}ms"));
                throw new TimeoutException($"[GEN040] 测试超时: {script.Name} (provider={provider}, attempts={maxAttempts}, durations=[{durationSummary}], inner={ex.Message})");
            }
            finally
            {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private static async Task<string> CaptureStderrTailAsync(DualRoleConversationRunner runner, int maxLen = 500)
    {
        try
        {
            var stderr = await runner.GetStderrOutputAsync().ConfigureAwait(true);
            var stderrTail = string.IsNullOrEmpty(stderr) ? "" : (stderr.Length > maxLen ? stderr[^maxLen..] : stderr);
            var diagSnapshot = await runner.GetDiagnosticSnapshotAsync().ConfigureAwait(true);
            return string.IsNullOrEmpty(stderrTail) ? diagSnapshot : $"{stderrTail}\n--- 诊断快照 ---\n{diagSnapshot}";
        }
        catch
        {
            return "(stderr捕获失败)";
        }
    }

    private void LogResult(ConversationResult result, VendorKind provider, TimeSpan elapsed)
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
            if (!result.AllPassed)
            {
                _output.WriteLine($"    RawOutput: {turn.RawOutput}");
                foreach (var err in turn.Errors)
                    _output.WriteLine($"    Error: {err}");
            }
        }

        foreach (var assert in result.AssertResults.Where(a => !a.IsPassed))
        {
            _output.WriteLine($"FAIL: {assert.Type} Expected=\"{assert.Expected}\" Desc=\"{assert.Description}\"");
        }

        if (!result.AllPassed && !string.IsNullOrWhiteSpace(result.StderrOutput))
        {
            var stderrPreview = result.StderrOutput.Length > 3000
                ? result.StderrOutput[..3000] + "...(truncated)"
                : result.StderrOutput;
            _output.WriteLine($"--- StderrOutput (len={result.StderrOutput.Length}):");
            _output.WriteLine(stderrPreview);
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