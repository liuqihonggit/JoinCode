namespace MockServer.E2E.Tests;

/// <summary>
/// 基础聊天命令 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// 包含 Help/Clear/Exit/Compact 等基础命令、会话管理命令、模式与限制命令
/// </summary>
public sealed class ChatCommandBasicCoverageTests : CoverageTestBase
{
    public ChatCommandBasicCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 聊天命令测试
    // ============================================================

    [Fact]
    public async Task HelpCommand_ShouldShowCommands()
    {
        await RunScriptAsync(ChatCommandScripts.HelpCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ClearCommand_ShouldResetConversation()
    {
        await RunScriptAsync(ChatCommandScripts.ClearCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExitCommand_ShouldExit()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ExitCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task CompactCommand_ShouldCompressContext()
    {
        await RunScriptAsync(ChatCommandConversationScripts.CompactCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task HistoryCommand_ShouldShowHistory()
    {
        await RunScriptAsync(ChatCommandConversationScripts.HistoryCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task VersionCommand_ShouldShowVersion()
    {
        await RunScriptAsync(ChatCommandConversationScripts.VersionCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task StatsCommand_ShouldShowStats()
    {
        await RunScriptAsync(ChatCommandConversationScripts.StatsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task CostCommand_ShouldShowCost()
    {
        await RunScriptAsync(ChatCommandConversationScripts.CostCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ModelCommand_ShouldShowModel()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ModelCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ToolsCommand_ShouldListTools()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ToolsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigCommand_ShouldShowConfig()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ConfigCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task TasksCommand_ShouldListTasks()
    {
        await RunScriptAsync(ChatCommandConversationScripts.TasksCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ResetCommand_ShouldResetConversation()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ResetCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task SessionCommand_ShouldShowSession()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SessionCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task EffortCommand_ShouldShowEffort()
    {
        await RunScriptAsync(ChatCommandConversationScripts.EffortCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ResumeCommand_ShouldResumeConversation()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ResumeCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 1a: Session 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task RewindCommand_ShouldRewindLastTurn()
    {
        await RunScriptAsync(ChatCommandConversationScripts.RewindCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ForkCommand_ShouldCreateFork()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ForkCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task BranchCommand_ShouldListBranches()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BranchCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task RenameCommand_ShouldRenameSession()
    {
        await RunScriptAsync(ChatCommandConversationScripts.RenameCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task BriefCommand_ShouldToggleBriefMode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.BriefCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task QuitCommand_ShouldExit()
    {
        await RunScriptAsync(ChatCommandConversationScripts.QuitCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 1b: Model 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task FastCommand_ShouldShowFastModeStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.FastCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ThinkbackCommand_ShouldShowThinkingReplay()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ThinkbackCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task PassesCommand_ShouldRedirectToPermissions()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PassesCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task OutputStyleCommand_ShouldShowDeprecationNotice()
    {
        await RunScriptAsync(ChatCommandConversationScripts.OutputStyleCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task RateLimitOptionsCommand_ShouldShowRateLimits()
    {
        await RunScriptAsync(ChatCommandConversationScripts.RateLimitOptionsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExtraUsageCommand_ShouldShowExtraUsage()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ExtraUsageCommand).ConfigureAwait(true);
    }
}
