// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace MockServer.E2E.Tests;

/// <summary>DualRole 对话 E2E 分组 A — 基础/工具/流式/SessionController（19测试,~97s）</summary>
public sealed class DualRoleConversationTestsA : DualRoleConversationTestBase {
    public DualRoleConversationTestsA(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task SingleTurn_TextOnly_ShouldGetResponse() {
        await RunScriptAsync(BasicConversationScripts.SingleTurnTextOnly).ConfigureAwait(true);
    }

    [Fact]
    public async Task SingleTurn_WithToolCall_ShouldShowToolExecution() {
        await RunScriptAsync(BasicConversationScripts.SingleTurnWithToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task MultiTurn_ThreeRounds_ShouldMaintainMemory() {
        await RunScriptAsync(BasicConversationScripts.MultiTurnMemory).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingResponse_ShouldReceiveChunks() {
        await RunScriptAsync(BasicConversationScripts.StreamingResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task NonInteractive_SinglePrompt_ShouldGetResponse() {
        await RunScriptAsync(BasicConversationScripts.SingleTurnNonInteractive).ConfigureAwait(true);
    }

    [Fact]
    public async Task BashToolCall_ShouldShowToolExecution() {
        await RunScriptAsync(ToolCallScripts.BashToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadFileToolCall_ShouldShowToolExecution() {
        await RunScriptAsync(ToolCallScripts.ReadFileToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCallThenFollowUp_ShouldMaintainContext() {
        await RunScriptAsync(MultiTurnScripts.ToolCallThenFollowUp).ConfigureAwait(true);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiToolCalls_ShouldShowAllToolExecutions() {
        await RunScriptAsync(ToolCallScripts.MultiToolCalls).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThinkingThenResponse_ShouldShowBoth() {
        await RunScriptAsync(ToolCallScripts.ThinkingThenResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task SequentialToolCalls_ShouldExecuteInOrder() {
        await RunScriptAsync(ToolIterationScripts.SequentialToolCalls).ConfigureAwait(true);
    }

    [Fact]
    public async Task MixedToolAndTextConversation_ShouldHandleBoth() {
        await RunScriptAsync(ToolIterationScripts.MixedToolAndTextConversation).ConfigureAwait(true);
    }

    [Fact]
    public async Task TokenUsage_Deserialization_ShouldNotThrow() {
        await RunScriptAsync(EdgeCaseScripts.TokenUsageNoError).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCall_PrefixCacheStable_ShouldDumpFiles() {
        var result = await RunScriptWithCacheAnalysisAsync(PrefixCacheScripts.ToolCallPrefixStable).ConfigureAwait(true);

        result.DumpFiles.Should().NotBeEmpty("应生成 dump 文件");
        result.CacheAnalysis.Should().NotBeNull("应有缓存分析结果");
        result.CacheAnalysis!.AllPrefixesStable.Should().BeTrue(
            $"前缀缓存应稳定。失效: {FormatCacheBreaks(result.CacheAnalysis)}");
    }

    // === Reasonix 移植功能 E2E 测试 ===

    [Fact]
    public async Task CompleteStep_MultiRound_ShouldShowMultipleSteps() {
        await RunScriptAsync(CompleteStepScripts.CompleteStepMultiRound).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolCall_ViaSessionController_ShouldWork() {
        await RunScriptAsync(SessionControllerScripts.ToolCallViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThinkingAndText_ViaSessionController_ShouldWork() {
        await RunScriptAsync(SessionControllerScripts.ThinkingAndTextViaController).ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamingComplexResponse_TruncatedJsonPath_ShouldNotCrash() {
        await RunScriptAsync(TruncatedJsonScripts.StreamingWithComplexResponse).ConfigureAwait(true);
    }

    [Fact]
    public async Task DualModel_DirectTextNoPlan_ShouldWork() {
        await RunScriptAsync(DualModelScripts.DirectTextNoPlan).ConfigureAwait(true);
    }
}
