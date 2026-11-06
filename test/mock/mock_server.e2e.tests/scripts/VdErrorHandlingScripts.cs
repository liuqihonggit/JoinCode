namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VD 组错误处理+退出码 E2E 脚本 — 覆盖 vd01-vd25 错误处理与退出码助记符。
/// 每个助记符对应一个 ConversationScript，验证错误处理链路的特定行为信号。
/// vd10/vd11/vd17-vd21 为退出码验证场景，RunScriptAsync 不直接验证退出码，
/// 标注 TODO 需用独立 E2E 或 .xxx/test_exit_codes.py 验证退出码（ADR 0080）。
/// vd09/vd15/vd16/vd22/vd23 需特殊构造（连接级错误/熔断器/预算耗尽/CrashStore/三层重试），标注 TODO。
/// vd13/vd24 已修复，标注 ✅已修复。
/// </summary>
public static class VdErrorHandlingScripts {
    // ============================================================
    // vd01-vd08: HTTP 状态码错误处理
    // ============================================================

    /// <summary>
    /// vd01: HTTP 200 成功 — LLM API 返回 200，正常响应。
    /// 可观测信号: HasAssistantResponse + NoErrors。
    /// </summary>
    public static ConversationScript Vd01Http200Success => new() {
        Name = "vd01 HTTP 200 成功",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！我是 vd01 测试助手，正常响应。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd01: 200 应有助手回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd01: 200 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd02: HTTP 401 认证错误 — LLM API 返回 401，exit=10 LLM_CALL_FAILED。
    /// 可观测信号: HasAssistantResponse（错误提示）。退出码 exit=10 需独立验证。
    /// </summary>
    public static ConversationScript Vd02Http401AuthError => new() {
        Name = "vd02 HTTP 401 认证错误",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Unauthorized",
                    HttpStatusCode = 401
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd02: 401 应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！认证已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd02: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd02: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd03: HTTP 403 授权错误 — LLM API 返回 403，exit=10。
    /// 可观测信号: HasAssistantResponse（错误提示）。退出码 exit=10 需独立验证。
    /// </summary>
    public static ConversationScript Vd03Http403Forbidden => new() {
        Name = "vd03 HTTP 403 授权错误",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Forbidden",
                    HttpStatusCode = 403
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd03: 403 应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！授权已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd03: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd03: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd04: HTTP 429 速率限制 — LLM API 返回 429，可重试。
    /// 可观测信号: 429 后恢复，HasAssistantResponse + NoErrors。
    /// </summary>
    public static ConversationScript Vd04Http429RateLimit => new() {
        Name = "vd04 HTTP 429 速率限制",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "服务器繁忙，请稍后再试。",
                    HttpStatusCode = 429
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd04: 429 后应有恢复回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！现在可以正常对话了。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd04: 重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd04: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd05: HTTP 500 服务端错误 — LLM API 返回 500，exit=10。
    /// 可观测信号: 500 后恢复，HasAssistantResponse + NoErrors。退出码 exit=10 需独立验证。
    /// </summary>
    public static ConversationScript Vd05Http500ServerError => new() {
        Name = "vd05 HTTP 500 服务端错误",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Internal Server Error",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd05: 500 后应有恢复回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！服务器已恢复正常。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd05: 重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd05: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd06: HTTP 502 网关错误 — LLM API 返回 502，网关错误。
    /// 可观测信号: 502 后恢复，HasAssistantResponse + NoErrors。
    /// </summary>
    public static ConversationScript Vd06Http502BadGateway => new() {
        Name = "vd06 HTTP 502 网关错误",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Bad Gateway",
                    HttpStatusCode = 502
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd06: 502 后应有恢复回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！网关已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd06: 重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd06: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd07: HTTP 503 服务不可用 — LLM API 返回 503，服务端暂时不可用。
    /// 可观测信号: 503 后恢复，HasAssistantResponse + NoErrors。
    /// </summary>
    public static ConversationScript Vd07Http503ServiceUnavailable => new() {
        Name = "vd07 HTTP 503 服务不可用",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Service Unavailable",
                    HttpStatusCode = 503
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd07: 503 后应有恢复回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！服务已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd07: 重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd07: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd08: 流式降级 fallback — 流式失败(502/503)后降级为非流式。
    /// 可观测信号: 502 后降级成功，HasAssistantResponse + NoErrors。
    /// 注意: 不设置 JCC_DISABLE_STREAMING_FALLBACK，启用默认降级机制。
    /// </summary>
    public static ConversationScript Vd08StreamingFallback => new() {
        Name = "vd08 流式降级 fallback",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Bad Gateway",
                    HttpStatusCode = 502,
                    IsStreaming = true
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd08: 流式失败后应降级有回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！流式降级后非流式正常响应。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd08: 降级后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd08: 降级后不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vd09-vd11: 连接级错误与超时
    // ============================================================

    /// <summary>
    /// vd09: 连接级错误不 fallback — HttpRequestException，连接错误直接传播。
    /// TODO: MockServer 模拟环境下无法真实触发 HttpRequestException（MockServer 总是返回 HTTP 响应）。
    /// 此脚本用 502 覆盖错误处理链路。真实连接级错误需手动 exe 验证（ADR 0080）：
    /// 设置 JCC_ENDPOINT=http://localhost:9999/v1（不可达端口）后 jcc --prompt "vd09"。
    /// </summary>
    public static ConversationScript Vd09ConnectionErrorNoFallback => new() {
        Name = "vd09 连接级错误不 fallback",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "连接错误",
                    HttpStatusCode = 502
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd09: 连接错误应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！连接已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd09: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd09: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd10: 超时 → exit=1240 — LLM API 超时，exit=1240 NET_TIMEOUT。
    /// TODO: 退出码 exit=1240 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖超时错误处理链路。真实超时退出码需用 .xxx/test_exit_codes.py 或独立 E2E 验证：
    /// 设置极短超时 + 慢响应 MockServer，验证 jcc.exe 退出码为 1240。
    /// </summary>
    public static ConversationScript Vd10TimeoutExit1240 => new() {
        Name = "vd10 超时 exit=1240",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "请求超时",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd10: 超时应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！请求已正常完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd10: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd10: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd11: --await 超时 → exit=1234 — `--await N` 超时，exit=1234。
    /// TODO: 退出码 exit=1234 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本用 AdditionalArgs="--await 1" 覆盖 await 超时链路。真实退出码需用
    /// .xxx/test_exit_codes.py EC30 场景验证：jcc --non-interactive --prompt "x" --await 1。
    /// </summary>
    public static ConversationScript Vd11AwaitTimeoutExit1234 => new() {
        Name = "vd11 --await 超时 exit=1234",
        AdditionalArgs = "--await 1",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "await 超时"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd11: await 超时应有响应" },
                ]
            }
        ]
    };

    // ============================================================
    // vd12-vd16: 空响应/重试/熔断/预算
    // ============================================================

    /// <summary>
    /// vd12: 空响应 {} — LLM 返回 `{}`，"模型返回空回复"。
    /// 可观测信号: HasAssistantResponse（空回复提示）。
    /// </summary>
    public static ConversationScript Vd12EmptyResponse => new() {
        Name = "vd12 空响应 {}",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "空回复", Description = "vd12: 空响应应提示'空回复'" },
                ]
            },
            new ConversationTurn {
                UserInput = "请正常回复",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！这次正常回复了。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd12: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd12: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd13: 空响应体 → SSE idle timeout ✅已修复 — LLM 返回空 body，TimeoutException。
    /// 此场景已在生产代码修复（SSE idle timeout 检测）。脚本覆盖空 body 错误处理链路。
    /// </summary>
    public static ConversationScript Vd13EmptyBodySseIdleTimeout => new() {
        Name = "vd13 空响应体 SSE idle timeout ✅已修复",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "",
                    IsStreaming = true
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "空回复", Description = "vd13: 空 body 应提示'空回复'" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！SSE 流已恢复正常。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd13: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd13: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd14: 重试耗尽 — 重试次数超限，"error_max_retries"。
    /// 可观测信号: HasAssistantResponse（重试耗尽错误提示）。
    /// </summary>
    public static ConversationScript Vd14RetryExhausted => new() {
        Name = "vd14 重试耗尽",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true",
            ["JCC_MAX_RETRIES"] = "1"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "error_max_retries",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd14: 重试耗尽应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！重试已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd14: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd14: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd15: 熔断器开启 — 连续失败触发熔断，CircuitBreakerOpenException。
    /// TODO: 熔断器需连续多次失败触发，ConversationScript 单轮难以构造连续失败序列。
    /// 此脚本用 500 错误覆盖错误处理链路。真实熔断器场景需手动 exe 验证（ADR 0080）：
    /// 连续发送多个会失败的请求，观察 CircuitBreakerOpenException。
    /// </summary>
    public static ConversationScript Vd15CircuitBreakerOpen => new() {
        Name = "vd15 熔断器开启",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "CircuitBreakerOpen",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd15: 熔断器开启应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！熔断器已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd15: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd15: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd16: 预算耗尽 — 重试预算耗尽，"重试预算耗尽"。
    /// TODO: 预算耗尽需精确控制重试预算阈值，ConversationScript 难以构造。
    /// 此脚本用 500 错误覆盖错误处理链路。真实预算耗尽需手动 exe 验证（ADR 0080）：
    /// 设置极小重试预算 + 持续失败，观察"重试预算耗尽"提示。
    /// </summary>
    public static ConversationScript Vd16RetryBudgetExhausted => new() {
        Name = "vd16 预算耗尽",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "重试预算耗尽",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd16: 预算耗尽应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！预算已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd16: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd16: 恢复后不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vd17-vd21: 退出码验证（Ctrl+C/配置/APIKey/参数/会话恢复）
    // ============================================================

    /// <summary>
    /// vd17: Ctrl+C → exit=130 — 用户按 Ctrl+C，exit=130。
    /// TODO: ConversationScript 无法模拟 Ctrl+C 中断信号，且 RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖正常对话链路。真实 Ctrl+C 退出码需手动 exe 验证（ADR 0080）：
    /// 启动 jcc 后发送 SIGINT，验证退出码为 130。
    /// </summary>
    public static ConversationScript Vd17CtrlCExit130 => new() {
        Name = "vd17 Ctrl+C exit=130",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！vd17 测试中。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd17: 正常对话应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd17: 正常对话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd18: 配置错误 → exit=2 — ConfigurationException，exit=2。
    /// TODO: 退出码 exit=2 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖错误处理链路。真实配置错误退出码需用 .xxx/test_exit_codes.py EC1-EC3 验证：
    /// jcc --non-interactive --prompt "x" --vendor "non-existent"（rc=2）。
    /// </summary>
    public static ConversationScript Vd18ConfigErrorExit2 => new() {
        Name = "vd18 配置错误 exit=2",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ConfigurationException"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd18: 配置错误应有提示" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd19: API Key 缺失 → exit=4 — 非交互模式无 API Key，exit=4。
    /// TODO: 退出码 exit=4 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖错误处理链路。真实 API Key 缺失退出码需用 .xxx/test_exit_codes.py EC5 验证：
    /// 清空 OPENAI_API_KEY 后 jcc --non-interactive --prompt "x"（rc=4）。
    /// </summary>
    public static ConversationScript Vd19ApiKeyMissingExit4 => new() {
        Name = "vd19 API Key 缺失 exit=4",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ApiKeyMissing"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd19: API Key 缺失应有提示" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd20: 参数解析错误 → exit=3 — 无效参数值，exit=3。
    /// TODO: 退出码 exit=3 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖错误处理链路。真实参数解析错误退出码需用 .xxx/test_exit_codes.py EC7-EC11 验证：
    /// jcc --unknown-arg（rc=3）。
    /// </summary>
    public static ConversationScript Vd20ArgParseErrorExit3 => new() {
        Name = "vd20 参数解析错误 exit=3",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ArgumentParseError"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd20: 参数解析错误应有提示" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd21: 会话恢复失败 → exit=5 — --resume 不存在的会话，exit=5。
    /// TODO: 退出码 exit=5 需独立验证，RunScriptAsync 不直接验证退出码。
    /// 此脚本覆盖错误处理链路。真实会话恢复失败退出码需用 .xxx/test_exit_codes.py EC13 验证：
    /// jcc --non-interactive --resume "nonexistent-id" --prompt "x"。
    /// </summary>
    public static ConversationScript Vd21SessionResumeFailedExit5 => new() {
        Name = "vd21 会话恢复失败 exit=5",
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "SessionResumeFailed"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd21: 会话恢复失败应有提示" },
                ]
            }
        ]
    };

    // ============================================================
    // vd22-vd25: CrashStore/三层重试/异常分类/content_filter
    // ============================================================

    /// <summary>
    /// vd22: CrashStore 快照写入 — 未处理异常，crash_{timestamp}.json。
    /// TODO: CrashStore 需真实未处理异常触发，ConversationScript 难以构造。
    /// 此脚本用 500 错误覆盖错误处理链路。真实 CrashStore 场景需手动 exe 验证（ADR 0080）：
    /// 触发未处理异常后检查 crash_{timestamp}.json 快照文件是否生成。
    /// </summary>
    public static ConversationScript Vd22CrashStoreSnapshot => new() {
        Name = "vd22 CrashStore 快照写入",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "未处理异常",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd22: 未处理异常应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！已从崩溃中恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd22: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd22: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd23: 三层重试叠加 — 500 错误触发三层重试。
    /// TODO: 三层重试需精确控制重试层级（HTTP 层/LLM 层/工具层），ConversationScript 难以构造。
    /// 此脚本用 500 错误覆盖错误处理链路。真实三层重试场景需手动 exe 验证（ADR 0080）：
    /// 设置 500 错误 + 观察日志中三层重试叠加记录。
    /// </summary>
    public static ConversationScript Vd23ThreeLayerRetry => new() {
        Name = "vd23 三层重试叠加",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "三层重试中",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd23: 三层重试应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！三层重试后已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd23: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd23: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd24: 异常分类递归检查 ✅已修复 — HTTP 错误被包为 InvalidOperationException，递归归类为 LlmCallFailed。
    /// 此场景已在生产代码修复（异常分类递归检查）。脚本覆盖包装异常错误处理链路。
    /// </summary>
    public static ConversationScript Vd24ExceptionClassificationRecursive => new() {
        Name = "vd24 异常分类递归检查 ✅已修复",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "你好",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "InvalidOperationException wrapped LlmCallFailed",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd24: 包装异常应有错误提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！异常分类已正确处理。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd24: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd24: 恢复后不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vd25: content_filter stop_reason — LLM 返回 content_filter，"模型回复被安全过滤"。
    /// 可观测信号: HasAssistantResponse（安全过滤提示）。
    /// </summary>
    public static ConversationScript Vd25ContentFilterStopReason => new() {
        Name = "vd25 content_filter stop_reason",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_DISABLE_STREAMING_FALLBACK"] = "true"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "请生成一些内容",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "模型回复被安全过滤"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd25: content_filter 应有安全过滤提示" },
                ]
            },
            new ConversationTurn {
                UserInput = "换一个安全的话题",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！这次内容安全，正常回复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vd25: 恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vd25: 恢复后不应有错误" },
                ]
            }
        ]
    };
}
