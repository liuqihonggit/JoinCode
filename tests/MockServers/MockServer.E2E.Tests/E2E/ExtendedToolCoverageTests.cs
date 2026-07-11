namespace MockServer.E2E.Tests;

/// <summary>
/// 扩展工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// 包含交互工具、结构化输出、Web 工具、步骤完成及 Phase 4 工具类别覆盖测试 (20 个类别)
/// </summary>
public sealed class ExtendedToolCoverageTests : CoverageTestBase
{
    public ExtendedToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 交互工具测试
    // ============================================================

    [Fact]
    public async Task AskUserQuestion_ShouldAskQuestion()
    {
        await RunScriptAsync(ExtendedToolScripts.AskUserQuestionTest).ConfigureAwait(true);
    }

    // ============================================================
    // 基础设施工具测试
    // ============================================================

    [Fact]
    public async Task StructuredOutputRegister_ShouldRegisterSchema()
    {
        await RunScriptAsync(InfrastructureToolScripts.StructuredOutputRegisterTest).ConfigureAwait(true);
    }

    // ============================================================
    // 网络工具测试
    // ============================================================

    [Fact]
    public async Task WebSearch_ShouldSearchWeb()
    {
        await RunScriptAsync(WebToolScripts.WebSearchTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task WebFetch_ShouldHandleSsrfGuard()
    {
        await RunScriptAsync(ExtendedToolScripts.WebFetchToolCall).ConfigureAwait(true);
    }

    // ============================================================
    // 进度步骤工具测试
    // ============================================================

    [Fact]
    public async Task CompleteStep_ToolCall_ShouldShowStepCompletion()
    {
        await RunScriptAsync(ExtendedToolScripts.CompleteStepToolTest).ConfigureAwait(true);
    }

    // ============================================================
    // Phase 4: 工具类别覆盖测试 (20 个类别)
    // ============================================================

    [Fact]
    public async Task McpListClients_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.McpListClientsTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteCsharpCode_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.ExecuteCsharpCodeTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task Snip_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.SnipTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task Monitor_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.MonitorTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task CtxInspect_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.CtxInspectTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task AnalyticsReport_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.AnalyticsReportTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task PolicyList_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.PolicyListTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task LspDocumentSymbols_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.LspDocumentSymbolsTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task GenerateCsharpCode_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.GenerateCsharpCodeTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task AnalyzeCsharpCode_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.AnalyzeCsharpCodeTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task VcrStatus_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.VcrStatusTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TerminalCapture_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.TerminalCaptureTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task Repl_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.ReplTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task SubscribePr_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.SubscribePrTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task ListPeers_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.ListPeersTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task WebBrowser_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.WebBrowserTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task VoiceStatus_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.VoiceStatusTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoteTrigger_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.RemoteTriggerTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task PushNotification_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.PushNotificationTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task PermissionListRules_Tool_ShouldWork()
    {
        await RunScriptAsync(ToolCoverageScripts.PermissionListRulesTest).ConfigureAwait(true);
    }
}
