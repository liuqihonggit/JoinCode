namespace MockServer.E2E.Tests;

/// <summary>
/// AST/CodeIndex 集成 E2E 测试 — 验证 jcc 启动时构造 AST 的性能和查询链路
/// 前置: SessionInitStep 显式触发 CodeIndexService.StartAsync(方案B)
/// 验证: stderr 输出 [STEP] CodeIndexService.StartAsync done, elapsed=Xms (性能计时)
/// 验证: code_index_stats / code_index_search / code_index_find_references / code_index_get_callers 4 个工具链路
/// </summary>
public sealed class AstIntegrationTests : CoverageTestBase
{
    public AstIntegrationTests(ITestOutputHelper output) : base(output) { }

    /// <summary>
    /// AST 启动构造并验证查询链路 — 覆盖 stats/search/find_references/get_callers
    /// WorkingDirectory 由 ResolveAstWorkingDirectory() 动态解析,让 AST 扫描真实代码,验证完整链路
    /// </summary>
    [Fact]
    public async Task AstStartupAndQueryLinks_ShouldVerifyCodeIndexChain()
    {
        await RunScriptAsync(AstIntegrationScripts.AstStartupAndQueryLinks).ConfigureAwait(true);
    }
}
