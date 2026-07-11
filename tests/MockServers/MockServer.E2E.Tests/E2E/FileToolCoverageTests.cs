namespace MockServer.E2E.Tests;

/// <summary>
/// 文件操作工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// </summary>
public sealed class FileToolCoverageTests : CoverageTestBase
{
    public FileToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 文件操作工具测试
    // ============================================================

    [Fact]
    public async Task WriteToolCall_ShouldSucceed()
    {
        await RunScriptAsync(MissingCoverageScripts.WriteToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task EditToolCall_ShouldSucceed()
    {
        await RunScriptAsync(MissingCoverageScripts.EditToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task GrepToolCall_ShouldShowResults()
    {
        await RunScriptAsync(MissingCoverageScripts.GrepToolCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task GlobToolCall_ShouldShowMatchingFiles()
    {
        await RunScriptAsync(MissingCoverageScripts.GlobToolCall).ConfigureAwait(true);
    }

    // ============================================================
    // 批量工具测试 — 每批覆盖多个同类工具
    // ============================================================

    [Fact]
    public async Task FileTools_Batch_ShouldCoverAll()
    {
        await RunScriptAsync(BatchFileToolScripts.FileToolsBatch).ConfigureAwait(true);
    }
}
