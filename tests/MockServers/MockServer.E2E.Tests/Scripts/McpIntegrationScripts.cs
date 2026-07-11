namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// MCP 集成 E2E 测试脚本 — 验证 jcc 作为 MCP 客户端连接外部 MCP 服务器并调用工具的正向链路
/// 链路: LLM(MockServer)返回 mcp_connect/mcp_call_tool 工具调用 → jcc 执行 → Mcp.MockServer 响应
/// </summary>
public static class McpIntegrationScripts
{
    /// <summary>
    /// MCP 正向链路验证 — 连接 Mcp.MockServer 并调用 echo 工具
    /// 链路: jcc → mcp_connect → Mcp.MockServer → mcp_call_tool(echo) → 返回结果
    /// 使用 {MCP_MOCK_PORT} 占位符,DualRoleConversationRunner 启动 Mcp.MockServer 后自动替换为实际端口
    /// </summary>
    public static ConversationScript McpConnectAndCallEcho => new()
    {
        Name = "MCP连接并调用echo工具",
        Mode = ConversationMode.Interactive,
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "连接MCP服务器",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_connect",
                            Arguments = """{"connection_name":"mock","endpoint":"http://localhost:{MCP_MOCK_PORT}/mcp","transport_type":"http"}"""
                        }
                    ],
                    FollowUpText = "已连接MCP服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "应调用mcp_connect工具" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "调用echo工具",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_call_tool",
                            Arguments = """{"connection_name":"mock","tool_name":"echo","arguments_json":"{\"message\":\"hello mcp\"}"}"""
                        }
                    ],
                    FollowUpText = "echo工具调用完成,返回了hello mcp。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "应调用mcp_call_tool工具" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "总结",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "MCP链路验证完成:已连接服务器并成功调用echo工具。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "MCP", Description = "应输出MCP相关总结" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
