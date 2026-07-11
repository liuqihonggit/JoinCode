namespace MockServer.E2E.Tests;

/// <summary>
/// 扩展/Bridge/平台/社交/生成 聊天命令 E2E 覆盖测试 — 拆分自 ChatCommandAdvancedCoverageTests 以启用 xUnit 集合并行
/// 包含 目标与主动 (2) + 扩展与集成 (5) + Bridge (2) + 平台集成 (4) + 社交与反馈 (5) + 简化模式 (1) + 生成与协作 (3) = 22 个测试
/// </summary>
public sealed class ChatCommandExtendedCoverageTests : CoverageTestBase
{
    public ChatCommandExtendedCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 阶段 2c: Task 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task GoalCommand_ShouldShowGoalStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.GoalCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ProactiveCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ProactiveCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 2d: Tools 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task McpCommand_ShouldListServers()
    {
        await RunScriptAsync(ChatCommandConversationScripts.McpCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task HooksCommand_ShouldListHooks()
    {
        await RunScriptAsync(ChatCommandConversationScripts.HooksCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task SkillsCommand_ShouldListSkills()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SkillsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task PluginCommand_ShouldListPlugins()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PluginCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task InstallCommand_ShouldShowInstallGuide()
    {
        await RunScriptAsync(ChatCommandConversationScripts.InstallCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 2e: Bridge 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task BridgeCommand_ShouldShowBridgeStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BridgeCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task BridgeKickCommand_ShouldShowUsage()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BridgeKickCommand).ConfigureAwait(true);
    }

    // ============================================================
    // Phase 3: Platform 类命令测试
    // ============================================================

    [Fact]
    public async Task ChromeCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ChromeCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task IdeCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.IdeCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task DesktopCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.DesktopCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task MobileCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.MobileCommand).ConfigureAwait(true);
    }

    // ============================================================
    // Phase 3: Social 类命令测试
    // ============================================================

    [Fact]
    public async Task BtwCommand_ShouldShowUsage()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BtwCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task FeedbackCommand_ShouldShowFeedbackPrompt()
    {
        await RunScriptAsync(ChatCommandConversationScripts.FeedbackCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ShareCommand_ShouldGenerateShareContent()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ShareCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task VoiceCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.VoiceCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task StickersCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.StickersCommand).ConfigureAwait(true);
    }

    // ============================================================
    // Phase 3: Other 类命令测试
    // ============================================================

    [Fact]
    public async Task SimpleCommand_ShouldToggleSimpleMode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SimpleCommand).ConfigureAwait(true);
    }

    // ============================================================
    // Phase 5: 补齐剩余未覆盖命令 — 2026-06-29 新增
    // ============================================================

    [Fact]
    public async Task GenerateCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.GenerateCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task PeersCommand_ShouldShowPeerStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PeersCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task InstallGitHubAppCommand_ShouldCheckGitHubCli()
    {
        await RunScriptAsync(ChatCommandConversationScripts.InstallGitHubAppCommand).ConfigureAwait(true);
    }
}
