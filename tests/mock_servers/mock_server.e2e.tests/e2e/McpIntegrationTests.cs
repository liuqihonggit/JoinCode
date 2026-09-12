namespace MockServer.E2E.Tests;

/// <summary>
/// MCP 集成 E2E 测试 — 验证 jcc 作为 MCP 客户端连接外部 MCP 服务器并调用工具的正向链路
/// 链路: LLM(MockServer)返回 mcp_connect/mcp_call_tool 工具调用 → jcc 执行 → Mcp.MockServer 响应
/// 独立测试类以启用 xUnit 集合并行
/// </summary>
public sealed class McpIntegrationTests : CoverageTestBase
{
    public McpIntegrationTests(ITestOutputHelper output) : base(output) { }

    /// <summary>
    /// MCP 正向链路验证 — 连接 Mcp.MockServer 并调用 echo 工具
    /// 验证: mcp_connect 工具调用 → jcc 连接 Mcp.MockServer → mcp_call_tool(echo) → 返回结果
    /// </summary>
    [Fact]
    public async Task McpConnectAndCallEcho_ShouldVerifyForwardChain()
    {
        await RunScriptAsync(McpIntegrationScripts.McpConnectAndCallEcho).ConfigureAwait(true);
    }
}
