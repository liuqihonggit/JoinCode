namespace MockServer.E2E.Tests;

/// <summary>
/// 代码与工作流聊天命令 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// 包含 Code 类命令、Config 类命令、Info 类命令
/// </summary>
public sealed class ChatCommandCodeCoverageTests : CoverageTestBase
{
    public ChatCommandCodeCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 阶段 1c: Code 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task ReviewCommand_ShouldReviewCode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ReviewCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task DiffFilesCommand_ShouldListChangedFiles()
    {
        await RunScriptAsync(ChatCommandConversationScripts.DiffFilesCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task FilesCommand_ShouldListFiles()
    {
        await RunScriptAsync(ChatCommandConversationScripts.FilesCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteCommand_ShouldExecuteCode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ExecuteCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task AnalyzeCommand_ShouldAnalyzeCode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.AnalyzeCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task AddDirCommand_ShouldAddDirectory()
    {
        await RunScriptAsync(ChatCommandConversationScripts.AddDirCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task SecurityReviewCommand_ShouldReviewSecurity()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SecurityReviewCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task CommitCommand_ShouldShowCommitInfo()
    {
        await RunScriptAsync(ChatCommandConversationScripts.CommitCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task WorktreeCommand_ShouldListWorktrees()
    {
        await RunScriptAsync(ChatCommandConversationScripts.WorktreeCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 1d: Config 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task ThemeCommand_ShouldSetTheme()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ThemeCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ColorCommand_ShouldShowColorTest()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ColorCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task VimCommand_ShouldToggleVimMode()
    {
        await RunScriptAsync(ChatCommandConversationScripts.VimCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task EnvCommand_ShouldShowEnvVars()
    {
        await RunScriptAsync(ChatCommandConversationScripts.EnvCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task SandboxToggleCommand_ShouldShowStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.SandboxToggleCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task PermissionsCommand_ShouldListPermissions()
    {
        await RunScriptAsync(ChatCommandConversationScripts.PermissionsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task InitCommand_ShouldQuickInit()
    {
        await RunScriptAsync(ChatCommandConversationScripts.InitCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task DoctorCommand_ShouldNotHang()
    {
        await RunScriptAsync(ChatCommandConversationScripts.DoctorCommand).ConfigureAwait(true);
    }

    // ============================================================
    // 阶段 1e: Info 类命令 E2E — 2026-06-28 新增
    // ============================================================

    [Fact]
    public async Task StatusCommand_ShouldShowStatus()
    {
        await RunScriptAsync(ChatCommandConversationScripts.StatusCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task UsageCommand_ShouldShowUsage()
    {
        await RunScriptAsync(ChatCommandConversationScripts.UsageCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task InsightsCommand_ShouldShowStats()
    {
        await RunScriptAsync(ChatCommandConversationScripts.InsightsCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReleaseNotesCommand_ShouldShowNotes()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ReleaseNotesCommand).ConfigureAwait(true);
    }

    [Fact]
    public async Task ContextCommand_ShouldShowContext()
    {
        await RunScriptAsync(ChatCommandConversationScripts.ContextCommand).ConfigureAwait(true);
    }
}
