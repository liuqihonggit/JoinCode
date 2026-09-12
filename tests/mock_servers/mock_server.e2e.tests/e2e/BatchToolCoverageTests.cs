namespace MockServer.E2E.Tests;

/// <summary>
/// 批量工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// 包含 Interaction/Code/Plan/System/Notebook/Worktree/Workflow/Skill/Team/Memory/Todo/Mcp 批量测试
/// </summary>
public sealed class BatchToolCoverageTests : CoverageTestBase
{
    public BatchToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 批量工具测试 — 每批覆盖多个同类工具
    // ============================================================

    [Fact]
    public async Task InteractionTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchInteractionToolScripts.InteractionToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task CodeTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchCodeToolScripts.CodeToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task PlanTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchPlanToolScripts.PlanToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task SystemTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchSystemToolScripts.SystemToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task NotebookTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchNotebookToolScripts.NotebookToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task WorktreeTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchWorktreeToolScripts.WorktreeToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task WorkflowTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchWorkflowToolScripts.WorkflowToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task SkillTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchSkillToolScripts.SkillToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task TeamTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchTeamToolScripts.TeamToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task MemoryTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchMemoryToolScripts.MemoryToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task TodoTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchTodoToolScripts.TodoToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task McpTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchMcpToolScripts.McpToolsBatch).ConfigureAwait(true);
    }
}
