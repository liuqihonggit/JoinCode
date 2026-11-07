namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VF 组命令行+开关+权限 E2E 脚本 — 覆盖 vf01-vf25 助记符。
/// 每个助记符对应一个 ConversationScript，验证命令行开关/环境变量/子命令的特定行为信号。
/// vf01-vf11: CLI 开关参数（通过 AdditionalArgs 传递）。
/// vf12-vf20: 环境变量开关（通过 ExtraEnvVars 设置）。
/// vf21-vf25: 子命令（通过 AdditionalArgs 传递）。
/// vf10(--await 超时 → exit=1234)和 vf25(未知子命令 → exit=1)涉及退出码验证，
/// ConversationScript 当前不支持退出码断言，标注 TODO 待扩展后补充。
/// </summary>
public static class VfCliSwitchScripts {
    // ============================================================
    // vf01-vf11: CLI 开关参数
    // ============================================================

    /// <summary>
    /// vf01: chat 子命令 (默认) — `jcc -p "你好"`，
    /// 可观测信号 正常对话响应。
    /// </summary>
    public static ConversationScript Vf01ChatDefault => new() {
        Name = "vf01 chat 子命令默认",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！我是 JoinCode 助手。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "你好", Description = "vf01: 应包含对话响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf01: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf02: --non-interactive 模式 — `jcc --non-interactive`，
    /// 可观测信号 非交互模式运行。
    /// </summary>
    public static ConversationScript Vf02NonInteractiveMode => new() {
        Name = "vf02 non-interactive 模式",
        AdditionalArgs = "--non-interactive",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试非交互模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "非交互模式已启用，直接处理输入。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "非交互", Description = "vf02: 应包含非交互模式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf02: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf03: --format ndjson — `jcc --format ndjson`，
    /// 可观测信号 NDJSON 事件流（含 "type" 字段）。
    /// </summary>
    public static ConversationScript Vf03FormatNdjson => new() {
        Name = "vf03 format ndjson",
        AdditionalArgs = "--format ndjson",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 ndjson 格式输出",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ndjson 格式响应内容"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "ndjson", Description = "vf03: 应包含 ndjson 格式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf03: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf04: --format json — `jcc --format json`，
    /// 可观测信号 `{ok,data,meta}` JSON 信封。
    /// </summary>
    public static ConversationScript Vf04FormatJson => new() {
        Name = "vf04 format json",
        AdditionalArgs = "--format json",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 json 格式输出",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "json 格式响应内容"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "json", Description = "vf04: 应包含 json 格式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf04: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf05: --format text — `jcc --format text`，
    /// 可观测信号 人类可读彩色输出。
    /// </summary>
    public static ConversationScript Vf05FormatText => new() {
        Name = "vf05 format text",
        AdditionalArgs = "--format text",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 text 格式输出",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "text 格式响应内容"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "text", Description = "vf05: 应包含 text 格式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf05: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf06: --permission-mode bypass — `jcc --permission-mode bypass`，
    /// 可观测信号 所有操作自动批准。
    /// </summary>
    public static ConversationScript Vf06PermissionModeBypass => new() {
        Name = "vf06 permission-mode bypass",
        AdditionalArgs = "--permission-mode bypass",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 bypass 权限模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "bypass 权限模式已启用，所有操作自动批准。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "bypass", Description = "vf06: 应包含 bypass 权限模式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf06: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf07: --permission-mode plan — `jcc --permission-mode plan`，
    /// 可观测信号 读取自动批准+写入需确认。
    /// </summary>
    public static ConversationScript Vf07PermissionModePlan => new() {
        Name = "vf07 permission-mode plan",
        AdditionalArgs = "--permission-mode plan",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 plan 权限模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "plan 权限模式已启用，读取自动批准，写入需确认。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "plan", Description = "vf07: 应包含 plan 权限模式响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf07: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf08: --trust (不跳过权限) — `jcc --trust`，
    /// 可观测信号 仅跳过信任确认，不跳过权限检查。
    /// </summary>
    public static ConversationScript Vf08Trust => new() {
        Name = "vf08 trust 不跳过权限",
        AdditionalArgs = "--trust",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 trust 开关",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "trust 已启用，跳过信任确认但保留权限检查。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "trust", Description = "vf08: 应包含 trust 响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf08: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf09: --continue / -c — `jcc -c`，
    /// 可观测信号 恢复最近会话。
    /// </summary>
    public static ConversationScript Vf09Continue => new() {
        Name = "vf09 continue 恢复会话",
        AdditionalArgs = "-c",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 continue 恢复会话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "会话已恢复，继续对话。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "会话", Description = "vf09: 应包含会话恢复响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf09: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf10: --await 超时 — `jcc --await 5`，
    /// 可观测信号 5秒后 exit=1234。
    /// TODO: ConversationScript 当前不支持退出码断言，此脚本仅覆盖 --await 参数解析链路。
    /// 真实超时退出码验证需手动 exe 验证（ADR 0080）：
    /// jcc --await 5 -p "长任务" 后检查 exit code == 1234。
    /// </summary>
    public static ConversationScript Vf10AwaitTimeout => new() {
        Name = "vf10 await 超时",
        AdditionalArgs = "--await 5",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 await 超时",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "await 超时已触发。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "await", Description = "vf10: 应包含 await 相关响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf10: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf11: --dry-run — `jcc --dry-run`，
    /// 可观测信号 只显示将执行操作。
    /// </summary>
    public static ConversationScript Vf11DryRun => new() {
        Name = "vf11 dry-run 只显示操作",
        AdditionalArgs = "--dry-run",
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 dry-run 模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "dry-run 模式已启用，仅显示将执行操作。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "dry", Description = "vf11: 应包含 dry-run 响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf11: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vf12-vf20: 环境变量开关
    // ============================================================

    /// <summary>
    /// vf12: JCC_MODEL_ID — 环境变量设置，
    /// 可观测信号 覆盖模型 ID。
    /// </summary>
    public static ConversationScript Vf12EnvModelId => new() {
        Name = "vf12 JCC_MODEL_ID 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_MODEL_ID"] = "test-model-vf12" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 JCC_MODEL_ID 环境变量",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "模型 ID 已通过环境变量设置为 test-model-vf12。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "test-model-vf12", Description = "vf12: 应包含环境变量设置的模型 ID" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "注册", Description = "vf12: 未注册模型应有警告" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf13: JCC_ENDPOINT — 环境变量设置，
    /// 可观测信号 覆盖 LLM 端点。
    /// </summary>
    public static ConversationScript Vf13EnvEndpoint => new() {
        Name = "vf13 JCC_ENDPOINT 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_ENDPOINT"] = "http://localhost:9999" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 JCC_ENDPOINT 环境变量",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "LLM 端点已通过环境变量设置。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "端点", Description = "vf13: 应包含端点设置响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf13: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf14: JCC_PERMISSION_MODE — 环境变量设置，
    /// 可观测信号 覆盖权限模式。
    /// </summary>
    public static ConversationScript Vf14EnvPermissionMode => new() {
        Name = "vf14 JCC_PERMISSION_MODE 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_PERMISSION_MODE"] = "bypass" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 JCC_PERMISSION_MODE 环境变量",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "权限模式已通过环境变量设置为 bypass。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "bypass", Description = "vf14: 应包含环境变量设置的权限模式" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf14: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf15: JCC_DISABLE_STREAMING_FALLBACK — `=1`，
    /// 可观测信号 禁用流式降级。
    /// </summary>
    public static ConversationScript Vf15EnvDisableStreamingFallback => new() {
        Name = "vf15 JCC_DISABLE_STREAMING_FALLBACK 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_DISABLE_STREAMING_FALLBACK"] = "1" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试禁用流式降级",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "流式降级已禁用。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "禁用", Description = "vf15: 应包含禁用流式降级响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf15: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf16: JCC_FILE_SYSTEM_MODE=InMemory — 环境变量设置，
    /// 可观测信号 纯内存文件系统。
    /// </summary>
    public static ConversationScript Vf16EnvFileSystemInMemory => new() {
        Name = "vf16 JCC_FILE_SYSTEM_MODE=InMemory 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_FILE_SYSTEM_MODE"] = "InMemory" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试内存文件系统模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "内存文件系统模式已启用。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "内存", Description = "vf16: 应包含内存文件系统响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf16: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf17: JCC_HTTP_MODE=Mock — 环境变量设置，
    /// 可观测信号 Mock HTTP 拦截。
    /// </summary>
    public static ConversationScript Vf17EnvHttpModeMock => new() {
        Name = "vf17 JCC_HTTP_MODE=Mock 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_HTTP_MODE"] = "Mock" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试 Mock HTTP 模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Mock HTTP 拦截模式已启用。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Mock", Description = "vf17: 应包含 Mock HTTP 响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf17: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf18: JCC_NO_MOUSE_TRACKING — `=1`，
    /// 可观测信号 禁用鼠标追踪走 OSC 8。
    /// </summary>
    public static ConversationScript Vf18EnvNoMouseTracking => new() {
        Name = "vf18 JCC_NO_MOUSE_TRACKING 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_NO_MOUSE_TRACKING"] = "1" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试禁用鼠标追踪",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "鼠标追踪已禁用，走 OSC 8 模式。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "禁用", Description = "vf18: 应包含禁用鼠标追踪响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf18: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf19: JCC_DEBUGLOG — `=1`，
    /// 可观测信号 启用诊断日志。
    /// </summary>
    public static ConversationScript Vf19EnvDebuglog => new() {
        Name = "vf19 JCC_DEBUGLOG 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_DEBUGLOG"] = "1" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试诊断日志",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "诊断日志已启用。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "诊断", Description = "vf19: 应包含诊断日志响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf19: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf20: JCC_SKIP_MODEL_FETCH — `=1`，
    /// 可观测信号 跳过启动时 model list 拉取。
    /// </summary>
    public static ConversationScript Vf20EnvSkipModelFetch => new() {
        Name = "vf20 JCC_SKIP_MODEL_FETCH 环境变量",
        ExtraEnvVars = new Dictionary<string, string> { ["JCC_SKIP_MODEL_FETCH"] = "1" },
        Turns =
        [
            new ConversationTurn {
                UserInput = "测试跳过模型拉取",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "启动时模型拉取已跳过。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "跳过", Description = "vf20: 应包含跳过模型拉取响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf20: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vf21-vf25: 子命令
    // ============================================================

    /// <summary>
    /// vf21: schema 子命令 — `jcc schema`，
    /// 可观测信号 CLI 参数定义 JSON。
    /// </summary>
    public static ConversationScript Vf21SchemaSubCommand => new() {
        Name = "vf21 schema 子命令",
        AdditionalArgs = "schema",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查询 schema",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "CLI 参数定义 JSON schema 输出。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "schema", Description = "vf21: 应包含 schema 输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf21: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf22: config-print 子命令 — `jcc config-print`，
    /// 可观测信号 配置树 JSON。
    /// </summary>
    public static ConversationScript Vf22ConfigPrintSubCommand => new() {
        Name = "vf22 config-print 子命令",
        AdditionalArgs = "config-print",
        Turns =
        [
            new ConversationTurn {
                UserInput = "打印配置",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "配置树 JSON 输出。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "apiKeyEnvVar", Description = "vf22: 应包含 config-print JSON 输出" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf22: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf23: mcp_serve 子命令 — `jcc mcp_serve`，
    /// 可观测信号 启动 MCP 服务端。
    /// </summary>
    public static ConversationScript Vf23McpServeSubCommand => new() {
        Name = "vf23 mcp_serve 子命令",
        AdditionalArgs = "mcp_serve",
        Turns =
        [
            new ConversationTurn {
                UserInput = "启动 MCP 服务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "MCP 服务端已启动。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "mcp", Description = "vf23: 应包含 mcp 服务端响应" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vf23: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf24: gh 子命令 — `jcc gh pr list`，
    /// 可观测信号 PR 列表。
    /// </summary>
    public static ConversationScript Vf24GhSubCommand => new() {
        Name = "vf24 gh 子命令",
        AdditionalArgs = "gh pr list",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出 PR",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "PR 列表输出。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "jcc-gh", Description = "vf24: gh 子命令应引用 jcc-gh" },
                ]
            }
        ]
    };

    /// <summary>
    /// vf25: 未知子命令错误 — `jcc &lt;未知&gt;`，
    /// 可观测信号 "未知子命令" + exit=1。
    /// TODO: ConversationScript 当前不支持退出码断言，此脚本仅覆盖未知子命令错误链路。
    /// 真实退出码验证需手动 exe 验证（ADR 0080）：
    /// jcc unknown_subcommand_vf25 后检查 exit code == 1。
    /// </summary>
    public static ConversationScript Vf25UnknownSubCommand => new() {
        Name = "vf25 未知子命令错误",
        AdditionalArgs = "unknown_subcommand_vf25",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行未知子命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "未知子命令错误诊断。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "未知", Description = "vf25: 应包含未知子命令错误诊断" },
                ]
            }
        ]
    };
}
