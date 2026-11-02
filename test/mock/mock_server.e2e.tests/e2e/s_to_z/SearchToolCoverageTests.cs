namespace MockServer.E2E.Tests;

/// <summary>
/// 搜索工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// </summary>
[Collection("MockServerShared")]
public sealed class SearchToolCoverageTests : CoverageTestBase {
    public SearchToolCoverageTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    // ============================================================
    // 批量工具测试 — 每批覆盖多个同类工具
    // ============================================================

    [Fact]
    public async Task SearchTools_Batch_ShouldCoverAll() {
        await RunScriptAsync(BatchSearchToolScripts.SearchToolsBatch).ConfigureAwait(true);
    }
}