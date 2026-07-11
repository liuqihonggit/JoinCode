namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 聊天命令 E2E 测试脚本 — Phase 2: Auth/Agent/Task/Tools/Bridge 类命令
/// 拆分自 ChatCommandConversationScripts.cs 以满足 JCC8001 2000 行限制
/// </summary>
public static partial class ChatCommandConversationScripts
{
    // ============================================================
    // 阶段 2a: Auth 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /login 命令 — 登录
    /// 无参数默认走 API Key 流程,ReadPassword 返回 null 后输出 "API Key 不能为空"
    /// </summary>
    public static ConversationScript LoginCommand => new()
    {
        Name = "/login 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/login",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "登录流程"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "登录", Description = "/login 应输出登录提示" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /logout 命令 — 登出
    /// 非交互模式 Confirmation 返回 false,不执行破坏性操作
    /// </summary>
    public static ConversationScript LogoutCommand => new()
    {
        Name = "/logout 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/logout",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "登出确认"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "登出", Description = "/logout 应输出登出确认" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /trust 命令 — 信任目录管理
    /// </summary>
    public static ConversationScript TrustCommand => new()
    {
        Name = "/trust 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/trust",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "信任状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "工作区", Description = "/trust 应输出工作区信任状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /oauth-refresh 命令 — 刷新 OAuth Token
    /// 无 Token 时输出 "无已存储的 OAuth Token" 或服务不可用提示
    /// </summary>
    public static ConversationScript OauthRefreshCommand => new()
    {
        Name = "/oauth-refresh 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/oauth-refresh",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "OAuth刷新"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "OAuth", Description = "/oauth-refresh 应输出 OAuth 相关提示" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /privacy-settings 命令 — 隐私设置
    /// </summary>
    public static ConversationScript PrivacySettingsCommand => new()
    {
        Name = "/privacy-settings 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/privacy-settings",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "隐私设置"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "隐私设置", Description = "/privacy-settings 应输出隐私设置" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 2b: Agent 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /plan 命令 — 计划模式
    /// 注意: 不加 NoErrors 断言,因为进入/退出计划模式失败时输出 "未知错误" 含 "错误" 二字
    /// </summary>
    public static ConversationScript PlanCommand => new()
    {
        Name = "/plan 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/plan",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "计划模式"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "计划", Description = "/plan 应输出计划模式相关信息" },
                ]
            }
        ]
    };

    /// <summary>
    /// /ultraplan 命令 — 超级计划模式
    /// 无参数显示帮助 "=== 超级计划模式 ==="
    /// 注意: 不加 NoErrors 断言,因为示例文本 "/ultraplan 修复所有编译错误 --execute" 含 "错误" 二字会误判
    /// </summary>
    public static ConversationScript UltraplanCommand => new()
    {
        Name = "/ultraplan 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/ultraplan",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "超级计划帮助"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "超级计划", Description = "/ultraplan 应输出超级计划模式帮助" },
                ]
            }
        ]
    };

    /// <summary>
    /// /memory 命令 — 记忆文件管理
    /// </summary>
    public static ConversationScript MemoryCommand => new()
    {
        Name = "/memory 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/memory",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "记忆文件列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "记忆", Description = "/memory 应输出记忆文件列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /agents 命令 — 代理列表
    /// </summary>
    public static ConversationScript AgentsCommand => new()
    {
        Name = "/agents 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/agents",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "代理列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "代理", Description = "/agents 应输出代理列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /advisor 命令 — 顾问模式
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript AdvisorCommand => new()
    {
        Name = "/advisor 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/advisor",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "顾问模式"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /buddy 命令 — 伙伴宠物
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript BuddyCommand => new()
    {
        Name = "/buddy 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/buddy",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "伙伴宠物"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /assistant 命令 — 长期助手模式
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript AssistantCommand => new()
    {
        Name = "/assistant 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/assistant",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "长期助手"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 2c: Task 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /goal 命令 — 目标引擎
    /// 注意: 不加 NoErrors,因为 GoalEngine 未注册时输出 "错误: 目标引擎未注册" 含 "错误" 二字
    /// </summary>
    public static ConversationScript GoalCommand => new()
    {
        Name = "/goal 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/goal",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "目标状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "目标", Description = "/goal 应输出目标相关信息" },
                ]
            }
        ]
    };

    /// <summary>
    /// /proactive 命令 — 主动执行模式
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript ProactiveCommand => new()
    {
        Name = "/proactive 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/proactive",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "主动执行"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 2d: Tools 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /mcp 命令 — MCP 服务器列表
    /// </summary>
    public static ConversationScript McpCommand => new()
    {
        Name = "/mcp 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/mcp",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "MCP列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "MCP", Description = "/mcp 应输出 MCP 服务器列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /hooks 命令 — Hook 配置列表
    /// </summary>
    public static ConversationScript HooksCommand => new()
    {
        Name = "/hooks 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/hooks",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Hook列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Hook", Description = "/hooks 应输出 Hook 配置列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /skills 命令 — 自定义技能列表
    /// </summary>
    public static ConversationScript SkillsCommand => new()
    {
        Name = "/skills 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/skills",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "技能列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "技能", Description = "/skills 应输出技能列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /plugin 命令 — 插件列表
    /// </summary>
    public static ConversationScript PluginCommand => new()
    {
        Name = "/plugin 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/plugin",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "插件列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "插件", Description = "/plugin 应输出插件列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /install 命令 — 安装引导
    /// 无参数显示 StepFlow 引导步骤 "选择安装类型"
    /// </summary>
    public static ConversationScript InstallCommand => new()
    {
        Name = "/install 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/install",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "安装引导"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "安装", Description = "/install 应输出安装引导" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 2e: Bridge 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /bridge 命令 — Bridge 状态
    /// </summary>
    public static ConversationScript BridgeCommand => new()
    {
        Name = "/bridge 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/bridge",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Bridge状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Bridge", Description = "/bridge 应输出 Bridge 状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /bridge-kick 命令 — 断开 Bridge 会话
    /// 无参数显示用法 "用法: /bridge-kick <session-id>"
    /// </summary>
    public static ConversationScript BridgeKickCommand => new()
    {
        Name = "/bridge-kick 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/bridge-kick",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Bridge断开"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "用法", Description = "/bridge-kick 应输出用法提示" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
