namespace MockServer.E2E.Tests;

/// <summary>
/// Shell 与 Git 工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// </summary>
public sealed class ShellGitToolCoverageTests : CoverageTestBase
{
    public ShellGitToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 批量工具测试 — 每批覆盖多个同类工具
    // ============================================================

    [Fact]
    public async Task ShellTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchShellToolScripts.ShellToolsBatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task GitTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchGitToolScripts.GitToolsBatch).ConfigureAwait(true);
    }
}
