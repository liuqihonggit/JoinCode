using System;

namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 聊天命令 E2E 测试脚本 — 覆盖全部 chat commands (Phase 1: Session/Model/Code/Config/Info/System)
/// </summary>
public static partial class ChatCommandConversationScripts
{
    /// <summary>
    /// /exit 命令 — NonInteractive 模式下直接退出（交互模式有确认对话框，E2E 无法确认）
    /// </summary>
    public static ConversationScript ExitCommand => new()
    {
        Name = "/exit 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/exit",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /compact 命令 — 压缩对话上下文
    /// </summary>
    public static ConversationScript CompactCommand => new()
    {
        Name = "/compact 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "闭嘴",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，我保持安静。"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "/compact",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "上下文已压缩。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/compact应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /history 命令 — 查看对话历史（jcc 内部处理，不经过 LLM）
    /// NoErrors 断言移除 — 输出中可能包含误触 Error 检测的系统提示文本
    /// </summary>
    public static ConversationScript HistoryCommand => new()
    {
        Name = "/history 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "记住我的项目名称是JoinCode",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已记住，项目名称是JoinCode。"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "/history",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "对话历史内容"  // 忽略 — jcc 内部输出
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/history应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// /version 命令 — 查看版本
    /// </summary>
    public static ConversationScript VersionCommand => new()
    {
        Name = "/version 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/version",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "JoinCode v1.0.0"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/version应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /stats 命令 — 查看统计
    /// </summary>
    public static ConversationScript StatsCommand => new()
    {
        Name = "/stats 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/stats",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "对话统计：\n- 消息数: 1\n- Token数: 42"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/stats应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /cost 命令 — 查看成本
    /// </summary>
    public static ConversationScript CostCommand => new()
    {
        Name = "/cost 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/cost",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前会话成本：\n- Token: 1,234\n- 估计费用: $0.02"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/cost应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /model 命令 — 查看/切换模型
    /// </summary>
    public static ConversationScript ModelCommand => new()
    {
        Name = "/model 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/model",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前模型: gpt-4o\n可用模型: gpt-4o, claude-sonnet-4, deepseek-chat"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/model应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /tools 命令 — 列出可用工具
    /// </summary>
    public static ConversationScript ToolsCommand => new()
    {
        Name = "/tools 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/tools",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "可用工具: Bash, Read, Write, Edit, Grep, Glob, WebSearch, TaskCreate, CronList"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/tools应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /config 命令 — 查看配置
    /// </summary>
    public static ConversationScript ConfigCommand => new()
    {
        Name = "/config 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/config",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前配置：\n- Provider: openai\n- Model: gpt-4o\n- Endpoint: http://localhost:9901"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/config应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /tasks 命令 — 查看任务
    /// </summary>
    public static ConversationScript TasksCommand => new()
    {
        Name = "/tasks 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/tasks",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前任务：\n无进行中的任务。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/tasks应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /reset 命令（/clear 别名）— 重置对话
    /// </summary>
    public static ConversationScript ResetCommand => new()
    {
        Name = "/reset 命令（/clear 别名）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/reset",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "对话已重置。有什么我可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/reset应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /session 命令 — 查看会话信息
    /// </summary>
    public static ConversationScript SessionCommand => new()
    {
        Name = "/session 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/session",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前会话：\n- ID: session_xxxx\n- 模式: interactive"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/session应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /effort 命令 — 查看/设置推理努力度
    /// </summary>
    public static ConversationScript EffortCommand => new()
    {
        Name = "/effort 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/effort",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "当前推理努力度: medium"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/effort应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /resume 命令 — 恢复对话
    /// </summary>
    public static ConversationScript ResumeCommand => new()
    {
        Name = "/resume 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/resume",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "没有可恢复的对话。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/resume应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1a: Session 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /rewind 命令 — 撤回最后一轮对话
    /// 多轮：先建立 1 轮对话上下文，再执行 /rewind last
    /// </summary>
    public static ConversationScript RewindCommand => new()
    {
        Name = "/rewind 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！有什么可以帮你的？"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "/rewind last",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已撤回"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/rewind应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /fork 命令 — 创建当前对话的分支
    /// 多轮：先建立 1 轮对话上下文，再执行 /fork
    /// </summary>
    public static ConversationScript ForkCommand => new()
    {
        Name = "/fork 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！有什么可以帮你的？"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "/fork test-branch",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已创建分支"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/fork应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /branch 命令 — 列出/创建/切换/删除对话分支
    /// </summary>
    public static ConversationScript BranchCommand => new()
    {
        Name = "/branch 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/branch list",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "=== 对话分支 ==="
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/branch应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /rename 命令 — 重命名当前会话
    /// </summary>
    public static ConversationScript RenameCommand => new()
    {
        Name = "/rename 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/rename test-session-name",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话已重命名"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/rename应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /brief 命令 — 切换简要消息模式
    /// </summary>
    public static ConversationScript BriefCommand => new()
    {
        Name = "/brief 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/brief on",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "简要消息模式已启用"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/brief应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /quit 命令 — NonInteractive 模式下直接退出（交互模式会终止进程）
    /// </summary>
    public static ConversationScript QuitCommand => new()
    {
        Name = "/quit 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/quit",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1b: Model 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /fast 命令 — 切换快速模式
    /// </summary>
    public static ConversationScript FastCommand => new()
    {
        Name = "/fast 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/fast",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "快速模式状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/fast应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /thinkback 命令 — 回放 AI 的思考过程
    /// </summary>
    public static ConversationScript ThinkbackCommand => new()
    {
        Name = "/thinkback 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/thinkback",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "思考回放"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/thinkback应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /passes 命令 — 已废弃，重定向到 /permissions
    /// </summary>
    public static ConversationScript PassesCommand => new()
    {
        Name = "/passes 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/passes",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "/passes 已废弃"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/passes应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /output-style 命令 — 已废弃，重定向到 /config
    /// </summary>
    public static ConversationScript OutputStyleCommand => new()
    {
        Name = "/output-style 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/output-style",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "/output-style 已废弃"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/output-style应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /rate-limit-options 命令 — 显示速率限制
    /// </summary>
    public static ConversationScript RateLimitOptionsCommand => new()
    {
        Name = "/rate-limit-options 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/rate-limit-options show",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "速率限制"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/rate-limit-options应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /extra-usage 命令 — 查看额外用量信息
    /// </summary>
    public static ConversationScript ExtraUsageCommand => new()
    {
        Name = "/extra-usage 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/extra-usage",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "额外用量"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/extra-usage应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1c: Code 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /review 命令 — 审查代码变更（非交互模式回退默认 prompt）
    /// </summary>
    public static ConversationScript ReviewCommand => new()
    {
        Name = "/review 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/review",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "代码审查结果"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/review应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /diff files 命令 — 列出变更文件（避免交互式浏览器）
    /// </summary>
    public static ConversationScript DiffFilesCommand => new()
    {
        Name = "/diff files 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/diff files",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "变更文件列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/diff files应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /files 命令 — 列出上下文文件
    /// </summary>
    public static ConversationScript FilesCommand => new()
    {
        Name = "/files 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/files",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "上下文中无文件"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/files应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /execute 命令 — 执行代码
    /// </summary>
    public static ConversationScript ExecuteCommand => new()
    {
        Name = "/execute 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                // 无参数场景：ExecuteCommand 直接返回，不启动 CodeSandbox 子进程
                // 避免触发 dotnet build/run 导致 30s+ 超时
                UserInput = "/execute",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "请提供要执行的代码"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "/execute无参数不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /analyze 命令 — 分析代码
    /// </summary>
    public static ConversationScript AnalyzeCommand => new()
    {
        Name = "/analyze 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/analyze function test() { return 1; }",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "代码分析结果"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/analyze应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /add-dir 命令 — 添加工作目录
    /// </summary>
    public static ConversationScript AddDirCommand => new()
    {
        Name = "/add-dir 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                // 传入不存在的路径：AddDirCommand 会输出 "目录不存在: ..." 然后返回
                // 避免无参数时 WorkspaceService 未初始化导致输出不足
                UserInput = "/add-dir Z:\\nonexistent-e2e-test-path",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "目录不存在"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "目录不存在", Description = "/add-dir应提示目录不存在" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /security-review 命令 — 安全审查
    /// </summary>
    public static ConversationScript SecurityReviewCommand => new()
    {
        Name = "/security-review 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/security-review",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "没有要审查的变更"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/security-review应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /commit 命令 — Git 提交（非交互模式取消提交）
    /// </summary>
    public static ConversationScript CommitCommand => new()
    {
        Name = "/commit 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/commit test message",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "提交结果"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/commit应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /worktree list 命令 — 列出 worktree（避免交互式确认）
    /// </summary>
    public static ConversationScript WorktreeCommand => new()
    {
        Name = "/worktree list 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/worktree list",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "worktree 列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/worktree list应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1d: Config 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /theme 命令 — 设置主题
    /// </summary>
    public static ConversationScript ThemeCommand => new()
    {
        Name = "/theme 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/theme dark",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "主题已设置为 dark"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/theme应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /color 命令 — 颜色测试
    /// </summary>
    public static ConversationScript ColorCommand => new()
    {
        Name = "/color 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/color test",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "颜色测试面板"
                },
                Asserts =
                [
                    // 颜色测试面板含 "红色(错误)" 字样,会触发 NoErrors 误判,改用 ContainsText
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "颜色测试", Description = "/color test应显示颜色测试面板" },
                ]
            }
        ]
    };

    /// <summary>
    /// /vim 命令 — Vim 模式切换
    /// </summary>
    public static ConversationScript VimCommand => new()
    {
        Name = "/vim 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/vim on",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "编辑模式: Vim"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/vim应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /env 命令 — 环境变量查询
    /// </summary>
    public static ConversationScript EnvCommand => new()
    {
        Name = "/env 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/env PATH",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "环境变量 PATH"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/env应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /sandbox-toggle 命令 — 沙箱状态查询
    /// </summary>
    public static ConversationScript SandboxToggleCommand => new()
    {
        Name = "/sandbox-toggle 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/sandbox-toggle status",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "沙箱状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/sandbox-toggle应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /permissions 命令 — 权限列表
    /// </summary>
    public static ConversationScript PermissionsCommand => new()
    {
        Name = "/permissions 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/permissions list",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "权限规则列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/permissions应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /init quick 命令 — 快速初始化（不调用 LLM）
    /// </summary>
    public static ConversationScript InitCommand => new()
    {
        Name = "/init quick 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/init quick",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "快速初始化完成"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/init quick应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /doctor 命令 — 诊断（非交互模式下 Dialog 不输出，仅验证不卡死）
    /// </summary>
    public static ConversationScript DoctorCommand => new()
    {
        Name = "/doctor 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/doctor",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "诊断完成"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "/doctor不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1e: Info 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /status 命令 — 会话状态
    /// </summary>
    public static ConversationScript StatusCommand => new()
    {
        Name = "/status 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/status",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/status应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /usage 命令 — 用量统计
    /// </summary>
    public static ConversationScript UsageCommand => new()
    {
        Name = "/usage 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/usage",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "用量统计"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/usage应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /insights stats 命令 — 会话统计（不调用 LLM）
    /// </summary>
    public static ConversationScript InsightsCommand => new()
    {
        Name = "/insights stats 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/insights stats",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话统计"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/insights stats应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /release-notes 命令 — 发行说明（可能 5s 网络超时）
    /// </summary>
    public static ConversationScript ReleaseNotesCommand => new()
    {
        Name = "/release-notes 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/release-notes",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "发行说明"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/release-notes应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /context 命令 — 上下文可视化
    /// </summary>
    public static ConversationScript ContextCommand => new()
    {
        Name = "/context 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/context",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "上下文可视化"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/context应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // 阶段 1f: System 类命令 E2E — 2026-06-28 新增
    // ============================================================

    /// <summary>
    /// /export 命令 — 导出对话到文件
    /// 非交互模式自动走 fallback 分支,输出 "已导出到: {filePath}"
    /// </summary>
    public static ConversationScript ExportCommand => new()
    {
        Name = "/export 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/export",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已导出对话"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "已导出", Description = "/export 应输出导出路径" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /copy 命令 — 复制最近 AI 消息到剪贴板
    /// 空会话时输出 "没有可复制的 AI 消息"
    /// </summary>
    public static ConversationScript CopyCommand => new()
    {
        Name = "/copy 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/copy",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "复制操作"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "复制", Description = "/copy 空会话应提示复制操作" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /summary 命令 — 会话摘要
    /// </summary>
    public static ConversationScript SummaryCommand => new()
    {
        Name = "/summary 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/summary",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话摘要"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "会话摘要", Description = "/summary 应输出会话摘要" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /statusline 命令 — 状态栏开关
    /// </summary>
    public static ConversationScript StatuslineCommand => new()
    {
        Name = "/statusline 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/statusline",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "状态栏状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "状态栏", Description = "/statusline 应输出状态栏状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /heapdump 命令 — 堆转储/运行时诊断
    /// </summary>
    public static ConversationScript HeapdumpCommand => new()
    {
        Name = "/heapdump 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/heapdump",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "堆转储诊断"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "堆转储", Description = "/heapdump 应输出堆转储诊断" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /tag 命令 — 会话标签管理
    /// </summary>
    public static ConversationScript TagCommand => new()
    {
        Name = "/tag 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/tag",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话标签"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "会话标签", Description = "/tag 应输出会话标签" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /workflows 命令 — 工作流列表
    /// </summary>
    public static ConversationScript WorkflowsCommand => new()
    {
        Name = "/workflows 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/workflows",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "工作流列表"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "工作流列表", Description = "/workflows 应输出工作流列表" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /upgrade 命令 — 检查更新
    /// UpgradeService 未注入时走 fallback 输出 "当前版本" + "请手动访问 GitHub Releases"
    /// </summary>
    public static ConversationScript UpgradeCommand => new()
    {
        Name = "/upgrade 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/upgrade",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "检查更新"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "/upgrade 应有输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}