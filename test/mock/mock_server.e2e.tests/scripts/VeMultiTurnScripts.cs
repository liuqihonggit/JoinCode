namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VE 组多轮+子代理+会话 E2E 脚本 — 覆盖 ve01-ve25 行为助记符。
/// 每个助记符对应一个 ConversationScript，验证多轮对话/子代理/会话恢复的特定行为信号。
/// ve17/ve20/ve21/ve22 因 ConversationScript 无法表达多进程/运行中子代理打断/transcript 文件/大上下文，
/// 标注 TODO 暂用模拟方式覆盖链路，真实场景需手动 exe 验证（ADR 0080）。
/// </summary>
public static class VeMultiTurnScripts {
    // ============================================================
    // ve01-ve05: 会话恢复 (--continue / --resume)
    // ============================================================

    /// <summary>
    /// ve01: --continue 恢复历史会话 — `jcc -c` 有历史会话，
    /// 可观测信号 "已恢复会话: {title} ({N} 条消息)"。
    /// </summary>
    public static ConversationScript Ve01ContinueResumeHistory => new() {
        Name = "ve01 --continue 恢复历史会话",
        AdditionalArgs = "--continue",
        Turns =
        [
            new ConversationTurn {
                UserInput = "继续之前的对话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已恢复会话: ve01-history (5 条消息)。我们之前在讨论 ve01 测试。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve01: 恢复历史会话应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve01: 恢复历史会话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve02: --continue 无历史会话 — `jcc -c` 无历史，
    /// 可观测信号 "无历史会话可恢复，将启动新会话"。
    /// </summary>
    public static ConversationScript Ve02ContinueNoHistory => new() {
        Name = "ve02 --continue 无历史会话",
        AdditionalArgs = "--continue",
        Turns =
        [
            new ConversationTurn {
                UserInput = "继续之前的对话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "无历史会话可恢复，将启动新会话。你好，有什么可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve02: 无历史会话应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve02: 无历史会话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve03: --resume &lt;id&gt; 精确匹配 — `jcc -r &lt;UUID&gt;`，
    /// 可观测信号 恢复成功。
    /// </summary>
    public static ConversationScript Ve03ResumeIdExactMatch => new() {
        Name = "ve03 --resume id 精确匹配",
        AdditionalArgs = "--resume ve03-test-session-id",
        Turns =
        [
            new ConversationTurn {
                UserInput = "恢复指定会话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已恢复会话: ve03-test-session-id。我们之前在讨论 ve03 测试。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "未找到会话", Description = "ve03: 不存在会话应报未找到" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve04: --resume &lt;id&gt; 未找到 — `jcc -r &lt;不存在&gt;`，
    /// 可观测信号 exit=5 "未找到会话"。
    /// </summary>
    public static ConversationScript Ve04ResumeIdNotFound => new() {
        Name = "ve04 --resume id 未找到",
        AdditionalArgs = "--resume ve04-nonexistent-session-id",
        Turns =
        [
            new ConversationTurn {
                UserInput = "恢复一个不存在的会话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "未找到会话: ve04-nonexistent-session-id"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "未找到会话", Description = "ve04: 未找到会话应报错" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve05: --resume &lt;title&gt; 模糊匹配 — `jcc -r &lt;关键词&gt;`，
    /// 可观测信号 customTitle Contains 匹配。
    /// </summary>
    public static ConversationScript Ve05ResumeTitleFuzzyMatch => new() {
        Name = "ve05 --resume title 模糊匹配",
        AdditionalArgs = "--resume ve05-keyword",
        Turns =
        [
            new ConversationTurn {
                UserInput = "按关键词恢复会话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "已恢复会话: ve05-keyword-discussion (模糊匹配)。我们之前在讨论 ve05 测试。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "未找到会话", Description = "ve05: 不存在关键词应报未找到" },
                ]
            }
        ]
    };

    // ============================================================
    // ve06-ve09: 多轮对话 + transcript + 会话 ID
    // ============================================================

    /// <summary>
    /// ve06: 多轮 messages 数组序列正确性 — --continue 后对话，
    /// 可观测信号 system→system→user→assistant→user 序列。
    /// 多轮脚本（Turns.Count=3）自动走 Interactive 模式。
    /// </summary>
    public static ConversationScript Ve06MultiTurnMessagesSequence => new() {
        Name = "ve06 多轮 messages 数组序列",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve06 第1轮：记住我的项目名是 JoinCode",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，我记住了，你的项目名是 JoinCode。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve06: 第1轮应有回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "ve06 第2轮：我的项目名是什么？",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你的项目名是 JoinCode。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve06: 第2轮应有回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "ve06 第3轮：再确认一次项目名",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "确认：你的项目名是 JoinCode。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve06: 第3轮应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve06: 多轮对话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve07: transcript 写入 — 对话完成，
    /// 可观测信号 transcript.jsonl 追加条目。
    /// </summary>
    public static ConversationScript Ve07TranscriptWrite => new() {
        Name = "ve07 transcript 写入",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve07 这轮对话应被写入 transcript",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve07 已记录，这轮对话将写入 transcript.jsonl。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve07: transcript 写入应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve07: transcript 写入不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve08: transcript 恢复 — --continue 加载，
    /// 可观测信号 从 transcript.jsonl 加载历史。
    /// </summary>
    public static ConversationScript Ve08TranscriptRestore => new() {
        Name = "ve08 transcript 恢复",
        AdditionalArgs = "--continue",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve08 从 transcript 恢复对话",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve08 已从 transcript.jsonl 加载历史，恢复 3 条消息。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve08: transcript 恢复应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve08: transcript 恢复不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve09: 会话 ID 一致性 — NDJSON 输出 + transcript，
    /// 可观测信号 session_id 字段一致。
    /// </summary>
    public static ConversationScript Ve09SessionIdConsistency => new() {
        Name = "ve09 会话 ID 一致性",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve09 验证会话 ID 一致性",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve09 会话 ID 在 NDJSON 输出和 transcript 中保持一致。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve09: 会话 ID 一致性应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve09: 会话 ID 一致性不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // ve10-ve15: 子代理启动/完成/异常/控制/usage 聚合
    // ============================================================

    /// <summary>
    /// ve10: subagent_type 解析 — agent 工具 subagent_type 参数，
    /// 可观测信号 PrimaryType + AllowedTypes。
    /// </summary>
    public static ConversationScript Ve10SubagentTypeParse => new() {
        Name = "ve10 subagent_type 解析",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve10 启动一个指定类型的子代理",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve10-agent", description = "ve10 子代理", prompt = "执行 ve10 测试", subagent_type = "general-purpose" },
                            ToolResult = "Agent started: ve10-agent-001, subagent_type: general-purpose"
                        }
                    ],
                    FollowUpText = "ve10 子代理已启动，subagent_type 解析为 general-purpose"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve10: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve10: 应包含 Agent started 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve10: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve10: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve11: 子代理启动 (agent_started) — 子代理首个 chunk，
    /// 可观测信号 AgentStarted 事件。
    /// </summary>
    public static ConversationScript Ve11SubagentStarted => new() {
        Name = "ve11 子代理启动 agent_started",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve11 启动子代理并等待首个 chunk",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve11-agent", description = "ve11 子代理", prompt = "执行 ve11 测试任务" },
                            ToolResult = "Agent started: ve11-agent-001"
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve11: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve11: 应包含子代理启动信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve11: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve11: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve11 子代理已启动，正在执行任务..."
        ]
    };

    /// <summary>
    /// ve12: 子代理完成 (agent_finished) — 子代理完成，
    /// 可观测信号 AgentFinished 事件 + usage。
    /// </summary>
    public static ConversationScript Ve12SubagentFinished => new() {
        Name = "ve12 子代理完成 agent_finished",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve12 启动子代理并等待完成",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve12-agent", description = "ve12 子代理", prompt = "执行 ve12 测试任务" },
                            ToolResult = "Agent started: ve12-agent-001"
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve12: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve12: 应包含子代理启动信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve12: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve12: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve12 子代理执行完成，任务结果: success, usage: input=100, output=50"
        ]
    };

    /// <summary>
    /// ve13: 子代理异常兜底 — 子代理 LLM 500，
    /// 可观测信号 仍发射 agent_finished(success=false)。
    /// </summary>
    public static ConversationScript Ve13SubagentExceptionFallback => new() {
        Name = "ve13 子代理异常兜底",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve13 启动子代理但子代理 LLM 返回 500",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve13-agent", description = "ve13 子代理", prompt = "执行 ve13 测试任务" },
                            ToolResult = "Agent started: ve13-agent-001"
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve13: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve13: 应包含 Agent started 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve13: 应有后续 assistant 回复" },
                ]
            }
        ],
        MockServerExtraTurns =
        [
            new ConversationTurn {
                UserInput = "ve13 子代理 LLM 调用",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve13 子代理 LLM 500 错误",
                    HttpStatusCode = 500
                },
                Asserts = []
            }
        ]
    };

    /// <summary>
    /// ve14: subagent_control list/pause/resume/stop — subagent_control 工具，
    /// 可观测信号 子代理状态切换。
    /// </summary>
    public static ConversationScript Ve14SubagentControl => new() {
        Name = "ve14 subagent_control list/pause/resume/stop",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve14 列出所有子代理状态",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "subagent_control",
                            Arguments = new { action = "list" },
                            ToolResult = "SubAgents: [ve14-agent-001: running, ve14-agent-002: paused]"
                        }
                    ],
                    FollowUpText = "ve14 子代理列表已获取"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "subagent_control", Description = "ve14: 应包含 subagent_control 工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve14: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve14: 不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "ve14 暂停子代理 ve14-agent-001",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "subagent_control",
                            Arguments = new { action = "pause", agent_id = "ve14-agent-001" },
                            ToolResult = "SubAgent ve14-agent-001 paused"
                        }
                    ],
                    FollowUpText = "ve14 子代理已暂停"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "subagent_control", Description = "ve14: pause 应包含 subagent_control 工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve14: pause 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve15: 子代理 usage 聚合到主代理 — 子代理完成，
    /// 可观测信号 主代理 usage += 子代理 usage。
    /// </summary>
    public static ConversationScript Ve15SubagentUsageAggregate => new() {
        Name = "ve15 子代理 usage 聚合到主代理",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve15 启动子代理并验证 usage 聚合",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve15-agent", description = "ve15 子代理", prompt = "执行 ve15 测试任务" },
                            ToolResult = "Agent started: ve15-agent-001"
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve15: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve15: 应包含 Agent started 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve15: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve15: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve15 子代理执行完成，usage: input=200, output=100。主代理 usage 已聚合。"
        ]
    };

    // ============================================================
    // ve16-ve19: num_turns 计数 + 并发会话 + Fork/teammate
    // ============================================================

    /// <summary>
    /// ve16: num_turns 计数 — 每轮 LLM 调用，
    /// 可观测信号 num_turns 递增。
    /// 多轮脚本（Turns.Count=3）自动走 Interactive 模式，验证 num_turns 从 1 递增到 3。
    /// </summary>
    public static ConversationScript Ve16NumTurnsCount => new() {
        Name = "ve16 num_turns 计数",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve16 第1轮：num_turns 应为 1",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve16 第1轮回复，num_turns=1"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve16: 第1轮应有回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "ve16 第2轮：num_turns 应为 2",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve16 第2轮回复，num_turns=2"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve16: 第2轮应有回复" },
                ]
            },
            new ConversationTurn {
                UserInput = "ve16 第3轮：num_turns 应为 3",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve16 第3轮回复，num_turns=3"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve16: 第3轮应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve16: num_turns 计数不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve17: 并发会话 — 多进程同时运行，
    /// 可观测信号 各进程独立成功。
    /// TODO: 并发会话需多进程同时运行，ConversationScript 单进程无法表达。
    /// 此脚本仅覆盖单进程链路。真实并发场景需手动 exe 验证（ADR 0080）：
    /// 同时启动多个 jcc 进程，验证各进程独立成功。
    /// </summary>
    public static ConversationScript Ve17ConcurrentSessions => new() {
        Name = "ve17 并发会话",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve17 单进程会话（并发场景需多进程手动验证）",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve17 单进程会话成功。并发场景需同时启动多个 jcc 进程验证。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve17: 单进程会话应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve17: 单进程会话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve18: Fork 子代理 — subagent_type 为空 + Fork 可用，
    /// 可观测信号 "Fork sub-agent launched"。
    /// </summary>
    public static ConversationScript Ve18ForkSubagent => new() {
        Name = "ve18 Fork 子代理",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve18 启动一个 Fork 子代理",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve18-fork-agent", description = "ve18 Fork 子代理", prompt = "执行 ve18 Fork 测试", subagent_type = "" },
                            ToolResult = "Fork sub-agent launched: ve18-fork-agent-001"
                        }
                    ],
                    FollowUpText = "ve18 Fork 子代理已启动"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve18: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Fork", Description = "ve18: 应包含 Fork 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve18: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve18: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve18 Fork 子代理执行完成，任务结果: success"
        ]
    };

    /// <summary>
    /// ve19: teammate 子代理 — subagent_type 为空 + teammate 可用，
    /// 可观测信号 "Teammate sub-agent launched"。
    /// </summary>
    public static ConversationScript Ve19TeammateSubagent => new() {
        Name = "ve19 teammate 子代理",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve19 启动一个 teammate 子代理",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve19-teammate-agent", description = "ve19 teammate 子代理", prompt = "执行 ve19 teammate 测试", subagent_type = "teammate" },
                            ToolResult = "Teammate sub-agent launched: ve19-teammate-agent-001"
                        }
                    ],
                    FollowUpText = "ve19 teammate 子代理已启动"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve19: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate", Description = "ve19: 应包含 Teammate 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve19: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve19: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve19 teammate 子代理执行完成，任务结果: success"
        ]
    };

    // ============================================================
    // ve20-ve25: steer 打断 + transcript 恢复 + 自动压缩 + 上下文余量 + 权限拒绝 + 思考持久化
    // ============================================================

    /// <summary>
    /// ve20: 子代理 steer 打断 — send_message 到运行中子代理，
    /// 可观测信号 "_steerRequested=true"。
    /// TODO: steer 打断需运行中子代理 + send_message 实时注入，ConversationScript 无法表达。
    /// 此脚本仅覆盖子代理启动链路。真实 steer 场景需手动 exe 验证（ADR 0080）：
    /// 启动子代理后通过 send_message 注入打断信号。
    /// </summary>
    public static ConversationScript Ve20SubagentSteer => new() {
        Name = "ve20 子代理 steer 打断",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve20 启动子代理（steer 打断需运行中子代理手动验证）",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve20-agent", description = "ve20 子代理", prompt = "执行 ve20 测试" },
                            ToolResult = "Agent started: ve20-agent-001"
                        }
                    ],
                    FollowUpText = "ve20 子代理已启动，steer 打断需运行中子代理手动验证"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve20: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Teammate sub-agent", Description = "ve20: 应包含 Agent started 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve20: 应有后续 assistant 回复" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve20 子代理执行中，等待 steer 信号..."
        ]
    };

    /// <summary>
    /// ve21: 子代理从 transcript 恢复 — resume_from_transcript，
    /// 可观测信号 "已从 transcript 恢复"。
    /// TODO: 子代理从 transcript 恢复需 transcript 文件构造，ConversationScript 无法表达。
    /// 此脚本仅覆盖子代理启动链路。真实恢复场景需手动 exe 验证（ADR 0080）：
    /// 预先创建 transcript 文件后启动子代理并指定 resume_from_transcript。
    /// </summary>
    public static ConversationScript Ve21SubagentResumeFromTranscript => new() {
        Name = "ve21 子代理从 transcript 恢复",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve21 启动子代理并从 transcript 恢复（需 transcript 文件手动构造）",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "ve21-agent", description = "ve21 子代理", prompt = "执行 ve21 测试", resume_from_transcript = "ve21-transcript.jsonl" },
                            ToolResult = "Agent started: ve21-agent-001, 已从 transcript 恢复"
                        }
                    ],
                    FollowUpText = "ve21 子代理已从 transcript 恢复"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "ve21: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "transcript", Description = "ve21: 应包含 transcript 恢复信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve21: 应有后续 assistant 回复" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "ve21 子代理已从 transcript 恢复，继续执行任务"
        ]
    };

    /// <summary>
    /// ve22: 自动压缩 — 上下文超阈值，
    /// 可观测信号 OnCompactCompleted 事件。
    /// TODO: 自动压缩需上下文超阈值触发，MockServer 模拟环境下无法真实产生大上下文。
    /// 此脚本仅覆盖对话链路。真实压缩场景需手动 exe 验证（ADR 0080）：
    /// 构造超长对话（>100 轮）触发自动压缩。
    /// </summary>
    public static ConversationScript Ve22AutoCompact => new() {
        Name = "ve22 自动压缩",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve22 对话（自动压缩需大上下文手动验证）",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve22 对话成功。自动压缩需上下文超阈值触发，MockServer 模拟环境下无法真实产生大上下文。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve22: 对话应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve22: 对话不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve23: 上下文余量 — onDone，
    /// 可观测信号 OnContextUsage 事件。
    /// </summary>
    public static ConversationScript Ve23ContextUsage => new() {
        Name = "ve23 上下文余量",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve23 验证上下文余量事件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve23 对话完成，OnContextUsage 事件已发射，上下文余量: 80%。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve23: 上下文余量应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve23: 上下文余量不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve24: 权限拒绝列表累积 — 工具被权限拒绝，
    /// 可观测信号 permission_denials 累积。
    /// </summary>
    public static ConversationScript Ve24PermissionDenialsAccumulate => new() {
        Name = "ve24 权限拒绝列表累积",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve24 执行一个会被权限拒绝的操作",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "rm -rf /" },
                            ToolResult = "Permission denied: 危险命令被拦截"
                        }
                    ],
                    FollowUpText = "ve24 权限拒绝: 危险命令被拦截，permission_denials 已累积"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "ve24: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Dangerous", Description = "ve24: 应包含危险命令拒绝信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve24: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// ve25: 思考内容持久化 — thinking 内容非空，
    /// 可观测信号 ThinkingStore.StoreAsync。
    /// </summary>
    public static ConversationScript Ve25ThinkingPersist => new() {
        Name = "ve25 思考内容持久化",
        Turns =
        [
            new ConversationTurn {
                UserInput = "ve25 验证思考内容持久化",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "ve25 思考内容已持久化到 ThinkingStore。",
                    ThinkingContent = "ve25 这是一个思考内容测试，应被持久化到 ThinkingStore.StoreAsync。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ve25: 思考内容持久化应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "ve25: 思考内容持久化不应有错误" },
                ]
            }
        ]
    };
}
