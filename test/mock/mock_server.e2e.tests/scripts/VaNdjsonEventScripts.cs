namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VA 组 NDJSON 事件输出 E2E 脚本 — 覆盖 va06/va08/va09 content block 类型。
/// va01-va05/va11-va15 由 Python 脚本 test_va_ndjson_events.py 验证（system/init + result 字段）。
/// va07（thinking block）和 va10（tool_progress）因 MockResponseType 不支持，标注 TODO 暂未覆盖。
/// </summary>
public static class VaNdjsonEventScripts {
    /// <summary>
    /// va06: assistant + text block — LLM 返回文本内容，
    /// 可观测信号 "type":"assistant","content":[{"type":"text","text":"..."}]。
    /// </summary>
    public static ConversationScript Va06TextBlock => new() {
        Name = "va06 assistant text block",
        Turns =
        [
            new ConversationTurn {
                UserInput = "请用一句话介绍你自己",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "我是一个AI助手，专门帮助用户解决问题。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "va06: 应有 assistant text block 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "va06: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// va08: assistant + tool_use block — LLM 返回 tool_calls，
    /// 可观测信号 "type":"tool_use","input":{"key":"value"}（input 是 JSON 对象非字符串）。
    /// </summary>
    public static ConversationScript Va08ToolUseBlock => new() {
        Name = "va08 assistant tool_use block",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo va08_tool_use" },
                            ToolResult = "va08_tool_use"
                        }
                    ],
                    FollowUpText = "当前目录已查看，输出 va08_tool_use。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "va08: 应包含 bash tool_use block" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "va08: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "va08: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// va09: user + tool_result block — 工具执行完成，
    /// 可观测信号 "type":"tool_result","is_error":false（is_error 是 bool 非 nullable）。
    /// 触发方式：LLM 返回 tool_calls → 工具执行 → 产生 tool_result block。
    /// </summary>
    public static ConversationScript Va09ToolResultBlock => new() {
        Name = "va09 user tool_result block",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取 test.txt 文件内容",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo va09_tool_result_content" },
                            ToolResult = "va09_tool_result_content"
                        }
                    ],
                    FollowUpText = "文件内容为 va09_tool_result_content。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "va09: 应触发 bash 工具调用产生 tool_result" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "bash", Description = "va09: tool_result is_error 应为 false" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "va09: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "va09: 不应有错误" },
                ]
            }
        ]
    };

    // TODO va07: assistant + thinking block — MockResponseType 不支持 thinking delta，
    //   需扩展 MockResponseType 增加 ThinkingOnly 或在 MockResponseScript 支持 ThinkingContent 注入。
    //   当前 MockResponseScript.ThinkingContent 字段存在但 MockServer 未实现 thinking delta 流式注入。
    //   可观测信号: "type":"thinking","thinking":"..."

    // TODO va10: tool_progress 事件 — MockResponseType 不支持 tool_progress，
    //   需扩展 MockServer 在工具执行期间产生 progress 事件。
    //   可观测信号: "type":"tool_progress"
}
