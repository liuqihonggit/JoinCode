namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 7 E2E 脚本 — P1 高频命令补覆盖（TASK065 第六波）
/// 包含: /debuglog -e、/??（需求澄清）、/sampling（无参查询）、/switch（无参显示模式）、/diag recent
/// </summary>
public static partial class ChatCommandConversationScripts {
    /// <summary>
    /// /debuglog -e 命令 — 仅显示错误段
    /// DebugLogRenderer.AppendErrors 固定输出 "═══ 错误 ═══" 段落标题；无错误时追加 "无错误记录"
    /// </summary>
    public static ConversationScript DebugLogErrorCommand => new() {
        Name = "/debuglog -e 命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/debuglog -e",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "错误", Description = "应包含错误段标题（═══ 错误 ═══）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /?? 命令 — 需求澄清模式（AskClarify）
    /// 关键: mock 响应文本必须含 ClarifyDoneMarker("【需求已明确】")，否则澄清循环进 ReadLine 卡 60s 超时
    /// 命令固定输出 "╔══ 需求澄清模式 ══╗" 横幅；响应含标记时输出 "✓ 需求澄清完成" 后退出子循环
    /// </summary>
    public static ConversationScript AskClarifyCommand => new() {
        Name = "/?? 需求澄清命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/?? 帮我写一个待办事项应用",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "【需求已明确】需求总结：开发一个本地待办事项应用，支持增删改查与完成状态标记。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "需求澄清", Description = "应包含需求澄清模式横幅" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /sampling 命令 — 无参查询当前采样参数
    /// SamplingCommand 无参时输出 "采样参数: 温度 {x}, 最大 {y} tokens"
    /// </summary>
    public static ConversationScript SamplingCommand => new() {
        Name = "/sampling 命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/sampling",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "采样参数", Description = "应包含采样参数查询输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /switch 命令 — 无参显示当前模式
    /// SwitchCommand 无参时 ShowCurrentMode 输出 "当前模式: 显示全部子代理输出"（默认）及活跃子代理列表
    /// </summary>
    public static ConversationScript SwitchCommand => new() {
        Name = "/switch 命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/switch",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "当前模式", Description = "应包含当前模式输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /diag recent 命令 — 崩溃快照查询
    /// E2E 隔离目录快照为空时 FormatReport 返回 "无崩溃快照记录。"；非空时输出 "崩溃快照报告（最近 …）"
    /// 两种输出均含 "崩溃快照"，据此断言查询正常执行
    /// </summary>
    public static ConversationScript DiagRecentCommand => new() {
        Name = "/diag recent 命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/diag recent",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "崩溃快照", Description = "应包含崩溃快照查询输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
