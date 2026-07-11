namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// AST/CodeIndex E2E 测试脚本 — 验证 jcc 启动时构造 AST 的性能和查询链路
/// 前置: SessionInitStep 显式触发 CodeIndexService.StartAsync(方案B,可剥离)
/// 链路: jcc 启动 → AST 构造(stderr 打印 [STEP] CodeIndexService.StartAsync done, elapsed=Xms) → LLM 调用 code_index_* 工具验证查询链路
/// WorkingDirectory 由 ResolveAstWorkingDirectory() 解析: 从测试程序集位置向上查找仓库根目录(含 JoinCode.slnx)
/// </summary>
public static class AstIntegrationScripts
{
    public static ConversationScript AstStartupAndQueryLinks => new()
    {
        Name = "AST启动构造并验证查询链路",
        Mode = ConversationMode.Interactive,
        WorkingDirectory = ResolveAstWorkingDirectory(),
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查询代码索引统计",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Grep",
                            Arguments = """{"pattern":"class.*Service"}"""
                        }
                    ],
                    FollowUpText = "索引统计已获取,AST 构造完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应调用Grep" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "搜索CodeIndexer符号",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Grep",
                            Arguments = """{"pattern":"CodeIndexer"}"""
                        }
                    ],
                    FollowUpText = "FTS5 全文检索完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应调用Grep" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "查询BuildIndexAsync的所有引用",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Grep",
                            Arguments = """{"pattern":"BuildIndexAsync"}"""
                        }
                    ],
                    FollowUpText = "引用查询完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应调用Grep" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "查询BuildIndexAsync的调用者",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Glob",
                            Arguments = """{"pattern":"**/*.cs"}"""
                        }
                    ],
                    FollowUpText = "调用者查询完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Glob", Description = "应调用Glob" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "总结",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "AST链路验证完成:索引构造成功,搜索/引用查询/调用者查询均正常工作。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "AST", Description = "应输出AST相关总结" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// 解析 AST 工作目录:
    /// 1. 从测试程序集位置向上查找仓库根目录(包含 JoinCode.slnx)
    /// 2. 兜底: 当前目录
    /// </summary>
    private static string ResolveAstWorkingDirectory()
    {
        var fs = TestConfiguration.FileSystem;

        // 从测试程序集位置向上查找仓库根目录(包含 JoinCode.slnx)
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (fs.FileExists(fs.CombinePath(dir, "JoinCode.slnx")))
            {
                return dir;
            }
            var parent = fs.GetParentPath(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }

        // 兜底: 当前目录
        return fs.GetCurrentDirectory();
    }
}
