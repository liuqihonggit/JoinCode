namespace MockServer.E2E.Tests;

/// <summary>
/// Agent / 子代理 spawn 工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// </summary>
public sealed class AgentToolCoverageTests : CoverageTestBase
{
    public AgentToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // Agent / 子代理 spawn 测试
    // ============================================================

    [Fact]
    public async Task AgentSpawn_ToolCall_ShouldSpawnSubAgent()
    {
        await RunScriptAsync(MissingCoverageScripts.AgentSpawnToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task AgentSpawnViaSpawnTool_ShouldWork()
    {
        await RunScriptAsync(MissingCoverageScripts.AgentSpawnViaTool).ConfigureAwait(true);
    }

    [Fact]
    public async Task AgentWorktreeIsolation_ShouldCreateWorktree()
    {
        await RunScriptAsync(MissingCoverageScripts.AgentWorktreeIsolation).ConfigureAwait(true);
    }

    // ============================================================
    // 批量工具测试 — 每批覆盖多个同类工具
    // ============================================================

    [Fact]
    public async Task AgentTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchAgentToolScripts.AgentToolsBatch).ConfigureAwait(true);
    }
}
