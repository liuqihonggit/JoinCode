namespace MockServer.E2E.Tests;

[Trait("Category", "Integration")]
public sealed class DualRoleConversationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;

    public DualRoleConversationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SingleTurn_TextOnly_ShouldGetResponse()
    {
        await RunScriptAsync(BasicConversationScripts.SingleTurnTextOnly).ConfigureAwait(true);
    }

    [Fact]
    public async Task SingleTurn_WithToolCall_ShouldShowToolExecution()
    {
        await RunScriptAsync(BasicConversationScripts.SingleTurnWithToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task MultiTurn_ThreeRounds_ShouldMaintainMemory()
    {
        await RunScriptAsync(BasicConversationScripts.MultiTurnMemory).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingResponse_ShouldReceiveChunks()
    {
        await RunScriptAsync(BasicConversationScripts.StreamingResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task NonInteractive_SinglePrompt_ShouldGetResponse()
    {
        await RunScriptAsync(BasicConversationScripts.SingleTurnNonInteractive).ConfigureAwait(true);
    }

    [Fact]
    public async Task BashToolCall_ShouldShowToolExecution()
    {
        await RunScriptAsync(ToolCallScripts.BashToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadFileToolCall_ShouldShowToolExecution()
    {
        await RunScriptAsync(ToolCallScripts.ReadFileToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task FiveRoundMemory_ShouldMaintainContext()
    {
        await RunScriptAsync(MultiTurnScripts.FiveRoundMemory).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptAsync(MultiTurnScripts.ToolCallThenFollowUp).ConfigureAwait(true);
    }

    [Fact]
    public async Task NegativeKeyword_ShouldGetResponse()
    {
        await RunScriptAsync(PromptInjectionScripts.NegativeKeyword).ConfigureAwait(true);
    }

    [Fact]
    public async Task KeepGoingKeyword_ShouldGetContinuation()
    {
        await RunScriptAsync(PromptInjectionScripts.KeepGoingKeyword).ConfigureAwait(true);
    }

    [Fact]
    public async Task NormalInput_ShouldNotTriggerInjection()
    {
        await RunScriptAsync(PromptInjectionScripts.NormalInputNoInjection).ConfigureAwait(true);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiToolCalls_ShouldShowAllToolExecutions()
    {
        await RunScriptAsync(ToolCallScripts.MultiToolCalls).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnknownToolCall_ShouldShowFailure()
    {
        await RunScriptAsync(ToolCallScripts.UnknownToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThinkingThenResponse_ShouldShowBoth()
    {
        await RunScriptAsync(ToolCallScripts.ThinkingThenResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallWithFollowUpText_ShouldShowBoth()
    {
        await RunScriptAsync(ToolCallScripts.ToolCallWithFollowUpText).ConfigureAwait(true);
    }

    [Fact]
    public async Task SequentialToolCalls_ShouldExecuteInOrder()
    {
        await RunScriptAsync(ToolIterationScripts.SequentialToolCalls).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallThenErrorRecovery_ShouldRecover()
    {
        await RunScriptAsync(ToolIterationScripts.ToolCallThenErrorRecovery).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeRoundToolIteration_ShouldMaintainContext()
    {
        await RunScriptAsync(ToolIterationScripts.ThreeRoundToolIteration).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallContextPreservation_ShouldRememberAfterToolCall()
    {
        await RunScriptAsync(ToolIterationScripts.ToolCallContextPreservation).ConfigureAwait(true);
    }

    [Fact]
    public async Task MixedToolAndTextConversation_ShouldHandleBoth()
    {
        await RunScriptAsync(ToolIterationScripts.MixedToolAndTextConversation).ConfigureAwait(true);
    }

    [Fact]
    public async Task TokenUsage_Deserialization_ShouldNotThrow()
    {
        await RunScriptAsync(EdgeCaseScripts.TokenUsageNoError).ConfigureAwait(true);
    }

    [Fact]
    public async Task LongStreamingResponse_ShouldReceiveFullContent()
    {
        await RunScriptAsync(EdgeCaseScripts.LongStreamingResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeTurn_PrefixCacheStable_ShouldDumpFiles()
    {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.ThreeTurnPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    [Fact]
    public async Task FiveTurn_PrefixCacheStable_ShouldDumpFiles()
    {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.FiveTurnPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    [Fact]
    public async Task ToolCall_PrefixCacheStable_ShouldDumpFiles()
    {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.ToolCallPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    // === Reasonix 移植功能 E2E 测试 ===

    [Fact]
    public async Task CompleteStep_ToolCall_ShouldShowStepCompletion()
    {
        await RunScriptAsync(CompleteStepScripts.CompleteStepToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task CompleteStep_MultiRound_ShouldShowMultipleSteps()
    {
        await RunScriptAsync(CompleteStepScripts.CompleteStepMultiRound).ConfigureAwait(true);
    }

    [Fact]
    public async Task WebFetch_ToolCall_ShouldHandleSsrfGuardPath()
    {
        await RunScriptAsync(SsrfGuardScripts.WebFetchToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingText_ViaSessionController_ShouldWork()
    {
        await RunScriptAsync(SessionControllerScripts.StreamingTextViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCall_ViaSessionController_ShouldWork()
    {
        await RunScriptAsync(SessionControllerScripts.ToolCallViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThinkingAndText_ViaSessionController_ShouldWork()
    {
        await RunScriptAsync(SessionControllerScripts.ThinkingAndTextViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingComplexResponse_TruncatedJsonPath_ShouldNotCrash()
    {
        await RunScriptAsync(TruncatedJsonScripts.StreamingWithComplexResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_ToolCallThenAnalysis_ShouldWork()
    {
        await RunScriptAsync(DualModelScripts.ToolCallThenAnalysis).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_MultiToolCallThenSynthesis_ShouldWork()
    {
        await RunScriptAsync(DualModelScripts.MultiToolCallThenSynthesis).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_DirectTextNoPlan_ShouldWork()
    {
        await RunScriptAsync(DualModelScripts.DirectTextNoPlan).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ThreeTurnContextPreservation_ShouldWork()
    {
        await RunScriptAsync(EventStreamScripts.ThreeTurnContextPreservation).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ToolProgressEventStream_ShouldWork()
    {
        await RunScriptAsync(EventStreamScripts.ToolProgressEventStream).ConfigureAwait(true);
    }

    private async Task RunScriptAsync(ConversationScript script)
    {
        await RunScriptWithRetryAsync(script).ConfigureAwait(true);
    }

    private async Task RunScriptWithRetryAsync(ConversationScript script, int maxAttempts = 2)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var runner = new DualRoleConversationRunner(
                _loggerFactory.CreateLogger<DualRoleConversationRunner>());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                var result = await runner.RunAsync(script, ProviderKind.OpenAI, timeoutCts.Token).ConfigureAwait(true);

                LogResult(result);

                if (result.AllPassed)
                    return;

                if (attempt < maxAttempts)
                {
                    _output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试失败，自动重试: {script.Name}");
                    continue;
                }

                result.AllPassed.Should().BeTrue($"所有断言应通过。失败: {FormatFailures(result)}");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试超时(>60s)，自动重试: {script.Name}");
                    continue;
                }
                throw new TimeoutException($"测试超时(>60s): {script.Name}");
            }
            finally
            {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private async Task<ConversationResult> RunScriptWithCacheAnalysisAsync(ConversationScript script)
    {
        return await RunScriptWithCacheAnalysisRetryAsync(script).ConfigureAwait(true);
    }

    private async Task<ConversationResult> RunScriptWithCacheAnalysisRetryAsync(ConversationScript script, int maxAttempts = 2)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var runner = new DualRoleConversationRunner(
                _loggerFactory.CreateLogger<DualRoleConversationRunner>());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                var result = await runner.RunAsync(script, ProviderKind.OpenAI, timeoutCts.Token).ConfigureAwait(true);

                LogResult(result);

                if (result.AllPassed)
                    return result;

                if (attempt < maxAttempts)
                {
                    _output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试失败，自动重试: {script.Name}");
                    continue;
                }

                result.AllPassed.Should().BeTrue($"所有断言应通过。失败: {FormatFailures(result)}");
                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试超时(>60s)，自动重试: {script.Name}");
                    continue;
                }
                throw new TimeoutException($"测试超时(>60s): {script.Name}");
            }
            finally
            {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }

        throw new InvalidOperationException("不应到达此处");
    }

    private void LogResult(ConversationResult result)
    {
        _output.WriteLine($"脚本: {result.ScriptName}");
        _output.WriteLine($"轮次数: {result.TurnRecords.Count}");
        _output.WriteLine($"断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败");

        foreach (var turn in result.TurnRecords)
        {
            _output.WriteLine($"--- Turn: UserInput=\"{turn.UserInput}\"");
            _output.WriteLine($"    ToolCalls: {turn.ToolCalls.Count}");
            _output.WriteLine($"    AssistantResponse: {turn.AssistantResponse[..Math.Min(100, turn.AssistantResponse.Length)]}...");
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

    private static string FormatCacheBreaks(PrefixCacheAnalysis analysis)
    {
        if (analysis.Breaks.Count == 0) return "(无)";
        return string.Join("; ", analysis.Breaks.Select(b =>
            $"Turn{b.FromTurn}->Turn{b.ToTurn}: {b.Reason}"));
    }
}
