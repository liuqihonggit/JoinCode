// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace MockServer.E2E.Tests;

/// <summary>DualRole 对话 E2E 分组 B — 多轮/注入/PrefixCache/DualModel/EventStream（19测试,~98s）</summary>
public sealed class DualRoleConversationTestsB : DualRoleConversationTestBase {
    public DualRoleConversationTestsB(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task FiveRoundMemory_ShouldMaintainContext() {
        await RunScriptAsync(MultiTurnScripts.FiveRoundMemory).ConfigureAwait(true);
    }

    [Fact]
    public async Task NegativeKeyword_ShouldGetResponse() {
        await RunScriptAsync(PromptInjectionScripts.NegativeKeyword).ConfigureAwait(true);
    }

    [Fact]
    public async Task KeepGoingKeyword_ShouldGetContinuation() {
        await RunScriptAsync(PromptInjectionScripts.KeepGoingKeyword).ConfigureAwait(true);
    }

    [Fact]
    public async Task NormalInput_ShouldNotTriggerInjection() {
        await RunScriptAsync(PromptInjectionScripts.NormalInputNoInjection).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnknownToolCall_ShouldShowFailure() {
        await RunScriptAsync(ToolCallScripts.UnknownToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallWithFollowUpText_ShouldShowBoth() {
        await RunScriptAsync(ToolCallScripts.ToolCallWithFollowUpText).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallThenErrorRecovery_ShouldRecover() {
        await RunScriptAsync(ToolIterationScripts.ToolCallThenErrorRecovery).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeRoundToolIteration_ShouldMaintainContext() {
        await RunScriptAsync(ToolIterationScripts.ThreeRoundToolIteration).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallContextPreservation_ShouldRememberAfterToolCall() {
        await RunScriptAsync(ToolIterationScripts.ToolCallContextPreservation).ConfigureAwait(true);
    }

    [Fact]
    public async Task LongStreamingResponse_ShouldReceiveFullContent() {
        await RunScriptAsync(EdgeCaseScripts.LongStreamingResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeTurn_PrefixCacheStable_ShouldDumpFiles() {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.ThreeTurnPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    [Fact]
    public async Task FiveTurn_PrefixCacheStable_ShouldDumpFiles() {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.FiveTurnPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    // === Reasonix 移植功能 E2E 测试 ===

    [Fact]
    public async Task CompleteStep_ToolCall_ShouldShowStepCompletion() {
        await RunScriptAsync(CompleteStepScripts.CompleteStepToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task WebFetch_ToolCall_ShouldHandleSsrfGuardPath() {
        await RunScriptAsync(SsrfGuardScripts.WebFetchToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingText_ViaSessionController_ShouldWork() {
        await RunScriptAsync(SessionControllerScripts.StreamingTextViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_ToolCallThenAnalysis_ShouldWork() {
        await RunScriptAsync(DualModelScripts.ToolCallThenAnalysis).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_MultiToolCallThenSynthesis_ShouldWork() {
        await RunScriptAsync(DualModelScripts.MultiToolCallThenSynthesis).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ThreeTurnContextPreservation_ShouldWork() {
        await RunScriptAsync(EventStreamScripts.ThreeTurnContextPreservation).ConfigureAwait(true);
    }

    [Fact]
    public async Task EventStream_ToolProgressEventStream_ShouldWork() {
        await RunScriptAsync(EventStreamScripts.ToolProgressEventStream).ConfigureAwait(true);
    }
}
