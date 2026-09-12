namespace MockServer.E2E.Tests;

/// <summary>
/// Auth/System/高级功能 聊天命令 E2E 覆盖测试 — 拆分自 ChatCommandAdvancedCoverageTests 以启用 xUnit 集合并行
/// 包含 System 类命令 (8) + Auth 类命令 (5) + 高级功能 (7) = 20 个测试
/// </summary>
public sealed class ChatCommandAuthExportCoverageTests : CoverageTestBase
{
    public ChatCommandAuthExportCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 阶段 1f: System 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task ExportCommand_ShouldExportConversation()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ExportCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task CopyCommand_ShouldShowNoCopyableMessage()
    {
        await RunScriptAsync(ChatCommandConversationScripts.CopyCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task SummaryCommand_ShouldShowSessionSummary()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SummaryCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task StatuslineCommand_ShouldShowStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.StatuslineCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task HeapdumpCommand_ShouldShowDiagnostics()
    {
        await RunScriptAsync(ChatCommandConversationScripts.HeapdumpCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagCommand_ShouldListTags()
    {
        await RunScriptAsync(ChatCommandConversationScripts.TagCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task WorkflowsCommand_ShouldListWorkflows()
    {
        await RunScriptAsync(ChatCommandConversationScripts.WorkflowsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task UpgradeCommand_ShouldShowCurrentVersion()
    {
        await RunScriptAsync(ChatCommandConversationScripts.UpgradeCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 2a: Auth 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task LoginCommand_ShouldShowLoginPrompt()
    {
        await RunScriptAsync(ChatCommandConversationScripts.LoginCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task LogoutCommand_ShouldShowConfirmPrompt()
    {
        await RunScriptAsync(ChatCommandConversationScripts.LogoutCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task TrustCommand_ShouldShowTrustStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.TrustCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task OauthRefreshCommand_ShouldShowOAuthStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.OauthRefreshCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task PrivacySettingsCommand_ShouldShowSettings()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PrivacySettingsCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 2b: Agent 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task PlanCommand_ShouldShowPlanModeInfo()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PlanCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task UltraplanCommand_ShouldShowHelp()
    {
        await RunScriptAsync(ChatCommandConversationScripts.UltraplanCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task MemoryCommand_ShouldShowMemoryFiles()
    {
        await RunScriptAsync(ChatCommandConversationScripts.MemoryCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task AgentsCommand_ShouldListAgents()
    {
        await RunScriptAsync(ChatCommandConversationScripts.AgentsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task AdvisorCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.AdvisorCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task BuddyCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BuddyCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task AssistantCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.AssistantCommand).ConfigureAwait(true);
    }
}
