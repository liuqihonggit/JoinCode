namespace JoinCode.Adapters;

/// <summary>
/// CLI 事件消费策略 — DX 模式使用 TerminalHelper 彩色输出，AX 模式输出 NDJSON 事件流
/// </summary>
public sealed class CliEventConsumer : IResettableEventConsumer {
    private readonly StringBuilder _fullResponse = new();
    private readonly StringBuilder _thinkingContent = new();
    private bool _isFirstToken = true;
    private readonly bool _agentMode;
    private readonly Cli.Output.CliOutputJsonContext _jsonContext;
    private readonly JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? _outputChannelManager;

    private readonly System.Collections.Generic.List<Cli.Output.SdkContentBlock> _pendingBlocks = [];
    private readonly System.Collections.Generic.List<Cli.Output.SdkToolResultBlock> _pendingToolResults = [];
    private readonly System.Collections.Generic.List<Cli.Output.SdkPermissionDenial> _permissionDenials = [];
    private readonly System.Collections.Generic.Dictionary<string, string?> _toolInputs = [];
    private string? _sessionId;
    private bool _systemInitSent;
    private string? _currentToolCallId;
    private long _toolStartTimestamp;

    /// <summary>获取权限拒绝列表 — 供 NonInteractiveModeRunner 填充到 SdkResultMessage</summary>
    public System.Collections.Generic.IReadOnlyList<Cli.Output.SdkPermissionDenial> PermissionDenials => _permissionDenials;

    /// <summary>默认构造函数 — DX 模式（彩色终端输出）</summary>
    public CliEventConsumer() {
        _agentMode = false;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
    }

    /// <summary>指定模式构造函数</summary>
    /// <param name="agentMode">true 表示 AX 模式（NDJSON 事件流），false 表示 DX 模式（彩色输出）</param>
    /// <param name="sessionId">会话 ID — 从 CliSession 传入，确保 NDJSON 输出的 session_id 与 transcript 目录名一致（-c 恢复会话依赖此一致性）</param>
    /// <param name="tools">可用工具名称列表 — 从 IToolRegistry 获取，填充 system/init 消息的 tools 字段</param>
    /// <param name="model">模型标识 — 从配置获取，填充 system/init 消息的 model 字段</param>
    /// <param name="version">jcc 版本号 — 从程序集获取，填充 system/init 消息的 claude_code_version 字段</param>
    /// <param name="mcpServers">MCP 服务器列表 — 从 DI 获取，填充 system/init 消息的 mcp_servers 字段</param>
    /// <param name="skills">技能列表 — 从 DI 获取，填充 system/init 消息的 skills 字段</param>
    /// <param name="agents">可用子代理列表 — 从 IAgentService 获取，填充 system/init 消息的 agents 字段</param>
    /// <param name="apiKeySource">API Key 来源（user/project/org/temporary/oauth）— 填充 system/init 消息的 apiKeySource 字段</param>
    /// <param name="betas">Beta 特性列表 — 填充 system/init 消息的 betas 字段</param>
    /// <param name="slashCommands">斜杠命令列表 — 从 ISlashCommandCatalog 获取，填充 system/init 消息的 slash_commands 字段</param>
    /// <param name="outputStyle">输出风格 — 从配置获取，填充 system/init 消息的 output_style 字段</param>
    /// <param name="plugins">插件列表 — 从 IPluginManager 获取，填充 system/init 消息的 plugins 字段</param>
    /// <param name="fastModeState">快速模式状态（off/cooldown/on）— 从 IFastModeService 获取，填充 system/init 消息的 fast_mode_state 字段</param>
    public CliEventConsumer(bool agentMode, string? sessionId = null, System.Collections.Generic.IReadOnlyList<string>? tools = null, string? model = null, string? version = null, System.Collections.Generic.IReadOnlyList<Cli.Output.SdkMcpServer>? mcpServers = null, System.Collections.Generic.IReadOnlyList<string>? skills = null, System.Collections.Generic.IReadOnlyList<string>? agents = null, string? apiKeySource = null, System.Collections.Generic.IReadOnlyList<string>? betas = null, System.Collections.Generic.IReadOnlyList<string>? slashCommands = null, string? outputStyle = null, System.Collections.Generic.IReadOnlyList<Cli.Output.SdkPlugin>? plugins = null, string? fastModeState = null) {
        _agentMode = agentMode;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
        _sessionId = sessionId;
        _tools = tools ?? [];
        _model = model;
        _version = version;
        _mcpServers = mcpServers ?? [];
        _skills = skills ?? [];
        _agents = agents ?? [];
        _apiKeySource = apiKeySource ?? "user";
        _betas = betas ?? [];
        _slashCommands = slashCommands ?? [];
        _outputStyle = outputStyle ?? "default";
        _plugins = plugins ?? [];
        _fastModeState = fastModeState;
    }

    private readonly System.Collections.Generic.IReadOnlyList<string> _tools = [];
    private readonly string? _model;
    private readonly string? _version;
    private readonly System.Collections.Generic.IReadOnlyList<Cli.Output.SdkMcpServer> _mcpServers = [];
    private readonly System.Collections.Generic.IReadOnlyList<string> _skills = [];
    private readonly System.Collections.Generic.IReadOnlyList<string> _agents = [];
    private readonly string _apiKeySource = "user";
    private readonly System.Collections.Generic.IReadOnlyList<string> _betas = [];
    private readonly System.Collections.Generic.IReadOnlyList<string> _slashCommands = [];
    private readonly string _outputStyle = "default";
    private readonly System.Collections.Generic.IReadOnlyList<Cli.Output.SdkPlugin> _plugins = [];
    private readonly string? _fastModeState;

    /// <summary>指定输出通道管理器构造函数 — DX 模式，通过通道管理器分发输出</summary>
    /// <param name="outputChannelManager">输出通道管理器（null 时退化为直接终端输出）</param>
    public CliEventConsumer(JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? outputChannelManager) {
        _agentMode = false;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
        _outputChannelManager = outputChannelManager;
    }

    /// <summary>累积的完整响应文本</summary>
    public string FullResponse => _fullResponse.ToString();

    /// <summary>累积的思考内容</summary>
    public string ThinkingContent => _thinkingContent.ToString();

    /// <summary>是否已收到首个 token</summary>
    public bool IsFirstToken => _isFirstToken;

    /// <summary>更新会话 ID — 恢复会话时由 CliSession.OverrideSessionId 调用，确保 NDJSON 输出的 session_id 与恢复的会话一致</summary>
    public void UpdateSessionId(string sessionId) => _sessionId = sessionId;

    /// <summary>重置状态（新一轮对话前调用）</summary>
    public void Reset() {
        _fullResponse.Clear();
        _thinkingContent.Clear();
        _isFirstToken = true;
        _pendingBlocks.Clear();
        _pendingToolResults.Clear();
        _permissionDenials.Clear();
        _toolInputs.Clear();
        _systemInitSent = false;
    }

    /// <summary>收到文本内容</summary>
    public void OnText(string content) {
        if (_isFirstToken) {
            _isFirstToken = false;
            if (!_agentMode && _outputChannelManager is null) {
                using var _label = TerminalHelper.SetColor(ConsoleColor.Cyan);
                TerminalHelper.WriteLine("[AI助手]");
            }
        }
        if (content.Length > 0) {
            _fullResponse.Append(content);
            if (_agentMode) {
                EnsureSystemInit();
                FlushPendingToolCalls();
                _pendingBlocks.Add(new Cli.Output.SdkTextBlock { Text = content });
            } else if (_outputChannelManager is not null) {
                _outputChannelManager.Write("main", "AI助手", content, JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);
            } else {
                using var _ = TerminalHelper.SetColor(ConsoleColor.Cyan);
                TerminalHelper.WriteRaw(content);
            }
        }
    }

    /// <summary>收到思考内容</summary>
    public void OnThinking(string thinking) {
        if (thinking.Length > 0) {
            _thinkingContent.Append(thinking);
            if (_agentMode) {
                EnsureSystemInit();
                FlushPendingToolCalls();
                _pendingBlocks.Add(new Cli.Output.SdkThinkingBlock { Thinking = thinking });
            }
        }
    }

    /// <summary>工具调用开始</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments) {
        if (toolCallId is not null)
            _toolInputs[toolCallId] = arguments;
        _currentToolCallId = toolCallId;
        _toolStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_agentMode) {
            EnsureSystemInit();
            _pendingBlocks.Add(new Cli.Output.SdkToolUseBlock {
                Id = toolCallId ?? string.Empty,
                Name = toolName,
                Input = ParseInputToJsonElement(arguments)
            });
        } else {
            using var _ = TerminalHelper.SetColor(ConsoleColor.DarkGray);
            TerminalHelper.NewLine();
            if (string.IsNullOrEmpty(arguments))
                TerminalHelper.WriteLine($"[Tool] {toolName}");
            else {
                var display = arguments.Length > 200 ? string.Concat(arguments.AsSpan(0, 200), "...") : arguments;
                TerminalHelper.WriteLine($"[Tool] {toolName}({display})");
            }
        }
    }

    /// <summary>工具调用结束</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) {
        if (isError && resultText is not null && resultText.Contains("拒绝")) {
            _toolInputs.TryGetValue(toolCallId ?? string.Empty, out var toolArgs);
            _permissionDenials.Add(new Cli.Output.SdkPermissionDenial {
                ToolName = toolName,
                ToolUseId = toolCallId ?? string.Empty,
                ToolInput = ParseInputToJsonElement(toolArgs)
            });
        }
        if (_agentMode) {
            _pendingToolResults.Add(new Cli.Output.SdkToolResultBlock {
                ToolUseId = toolCallId ?? string.Empty,
                Content = resultText ?? string.Empty,
                IsError = isError
            });
        } else {
            using var _ = TerminalHelper.SetColor(isError ? ConsoleColor.Red : ConsoleColor.DarkGray);
            var status = isError ? ToolExecutionStatus.Fail : ToolExecutionStatus.Ok;
            var glyph = status.ToValue();
            TerminalHelper.WriteLine($"[{glyph}] {toolName}");
            var displayText = ToolErrorFormatter.ExtractMessage(resultText, isError);
            if (!string.IsNullOrEmpty(displayText))
                WriteToolEndText(displayText, isError);
        }
        _currentToolCallId = null;
        _toolStartTimestamp = 0;
    }

    private static void WriteToolEndText(string displayText, bool isError) {
        var lines = displayText.Split('\n');
        var maxLines = isError ? 15 : 20;
        var displayCount = Math.Min(lines.Length, maxLines);
        for (var i = 0; i < displayCount; i++)
            TerminalHelper.WriteLine($"  {lines[i].TrimEnd('\r')}");
        if (lines.Length > maxLines)
            TerminalHelper.WriteLine($"  ... ({lines.Length} lines total, showing first {maxLines})");
    }

    /// <summary>工具调用进度</summary>
    public void OnToolProgress(string toolName, string progressType, string? progressMessage) {
        if (_agentMode) {
            WriteSdkJson(Cli.Output.SdkToolProgressMessage.Create(_sessionId) with {
                ToolName = toolName,
                ToolUseId = _currentToolCallId ?? string.Empty,
                ElapsedTimeSeconds = _toolStartTimestamp > 0 ? System.Diagnostics.Stopwatch.GetElapsedTime(_toolStartTimestamp).TotalSeconds : 0,
            });
        } else {
            TerminalHelper.WriteLine($"[...] {toolName}: {progressMessage}");
        }
    }

    /// <summary>检测到循环输出</summary>
    public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) {
        if (!_agentMode) {
            TerminalHelper.WriteLine($"[Loop] 检测到循环输出(第{triggerCount}次)");
        }
    }

    /// <summary>计时摘要</summary>
    public void OnTimingSummary(string summary) {
        if (!_agentMode) {
            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine(summary);
        }
    }

    /// <summary>流式响应完成</summary>
    public void OnDone(TokenUsage? usage, string? modelId, string? stopReason = null) {
        if (_agentMode) {
            FlushAssistant();
            FlushToolResults();
        } else {
            TerminalHelper.WriteLine();
            try { System.Console.Out.Flush(); } catch (IOException) { System.Diagnostics.Debug.WriteLine("[CliEventConsumer] stdout flush failed"); }
        }
    }

    /// <summary>子代理启动</summary>
    public void OnSubagentStarted(string subagentId, string agentType, string description, string? role = null) { }

    /// <summary>子代理工具活动</summary>
    public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) { }

    /// <summary>子代理完成</summary>
    public void OnSubagentFinished(string subagentId, bool isError, int tokenTotal, TokenUsage? usage = null) { }

    /// <summary>上下文窗口余量</summary>
    public void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) { }

    /// <summary>会话状态变更 — NDJSON 模式输出 SdkSessionStateChangedMessage</summary>
    public void OnSessionStateChanged(string state) {
        if (_agentMode) {
            EnsureSystemInit();
            WriteSdkJson(Cli.Output.SdkSessionStateChangedMessage.Create(_sessionId, state));
        }
    }

    /// <summary>自动重试 — NDJSON 模式输出 SdkApiRetryMessage</summary>
    public void OnRetry(string reason, int attempt, int maxRetries, int delayMs) {
        if (_agentMode) {
            EnsureSystemInit();
            WriteSdkJson(Cli.Output.SdkApiRetryMessage.Create(_sessionId, attempt, maxRetries, delayMs, reason));
        }
    }

    /// <summary>上下文压缩完成 — NDJSON 模式输出 SdkCompactBoundaryMessage</summary>
    public void OnCompactCompleted(int messagesSummarized, long originalTokens, long compressedTokens, bool isAuto) {
        if (_agentMode) {
            EnsureSystemInit();
            WriteSdkJson(Cli.Output.SdkCompactBoundaryMessage.Create(_sessionId, new Cli.Output.SdkCompactMetadata {
                Trigger = isAuto ? "auto" : "manual",
                PreTokens = originalTokens,
            }));
        }
    }

    /// <summary>确保 system/init 消息已发送（首次事件时发送）</summary>
    private void EnsureSystemInit() {
        if (_systemInitSent) return;
        _systemInitSent = true;
        _sessionId ??= System.Guid.NewGuid().ToString("N");
        WriteSdkJson(Cli.Output.SdkSystemInitMessage.Create(_sessionId) with {
            ClaudeCodeVersion = _version ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
            Cwd = System.IO.Directory.GetCurrentDirectory(),
            Model = _model ?? System.Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "unknown",
            Tools = _tools.Count > 0 ? new System.Collections.Generic.List<string>(_tools) : [],
            McpServers = _mcpServers.Count > 0 ? new System.Collections.Generic.List<Cli.Output.SdkMcpServer>(_mcpServers) : [],
            PermissionMode = System.Environment.GetEnvironmentVariable("JCC_PERMISSION_MODE") ?? "default",
            Skills = _skills.Count > 0 ? new System.Collections.Generic.List<string>(_skills) : [],
            Agents = _agents.Count > 0 ? new System.Collections.Generic.List<string>(_agents) : [],
            ApiKeySource = _apiKeySource,
            Betas = _betas.Count > 0 ? new System.Collections.Generic.List<string>(_betas) : [],
            SlashCommands = _slashCommands.Count > 0 ? new System.Collections.Generic.List<string>(_slashCommands) : [],
            OutputStyle = _outputStyle,
            Plugins = _plugins.Count > 0 ? new System.Collections.Generic.List<Cli.Output.SdkPlugin>(_plugins) : [],
            FastModeState = _fastModeState,
        });
    }

    /// <summary>Flush 累积的 assistant 消息（如果有 content blocks），合并相邻同类型 block</summary>
    private void FlushAssistant() {
        if (_pendingBlocks.Count == 0) return;
        var merged = MergeAdjacentBlocks(_pendingBlocks);
        WriteSdkJson(Cli.Output.SdkAssistantMessage.Create(new Cli.Output.SdkMessageBody {
            Role = "assistant",
            Content = merged
        }, _sessionId) with {
            ParentToolUseId = null,
        });
        _pendingBlocks.Clear();
    }

    /// <summary>Flush 累积的 tool_result 作为单个 user 消息（对齐基准 — 多 tool_result 合并到同一 user 消息）</summary>
    private void FlushToolResults() {
        if (_pendingToolResults.Count == 0) return;
        WriteSdkJson(Cli.Output.SdkUserMessage.Create(new Cli.Output.SdkMessageBody {
            Role = "user",
            Content = _pendingToolResults.Cast<Cli.Output.SdkContentBlock>().ToList()
        }, _sessionId));
        _pendingToolResults.Clear();
    }

    /// <summary>Flush pending tool calls（先 assistant[tool_use...] 再 user[tool_result...]），在非工具事件时调用</summary>
    private void FlushPendingToolCalls() {
        if (_pendingToolResults.Count == 0) return;
        FlushAssistant();
        FlushToolResults();
    }

    /// <summary>合并相邻同类型 block（text+text→text, thinking+thinking→thinking）</summary>
    private static System.Collections.Generic.List<Cli.Output.SdkContentBlock> MergeAdjacentBlocks(
        System.Collections.Generic.List<Cli.Output.SdkContentBlock> blocks) {
        var result = new System.Collections.Generic.List<Cli.Output.SdkContentBlock>(blocks.Count);
        foreach (var block in blocks) {
            if (result.Count > 0 && block is Cli.Output.SdkTextBlock text && result[^1] is Cli.Output.SdkTextBlock prevText) {
                result[^1] = new Cli.Output.SdkTextBlock { Text = prevText.Text + text.Text };
            } else if (result.Count > 0 && block is Cli.Output.SdkThinkingBlock thinking && result[^1] is Cli.Output.SdkThinkingBlock prevThinking) {
                result[^1] = new Cli.Output.SdkThinkingBlock { Thinking = prevThinking.Thinking + thinking.Thinking };
            } else {
                result.Add(block);
            }
        }
        return result;
    }

    /// <summary>将工具参数 JSON 字符串解析为 JsonElement，使序列化输出 JSON 对象而非字符串</summary>
    private static System.Text.Json.JsonElement ParseInputToJsonElement(string? json) {
        using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    /// <summary>写入 SDK JSON 消息到 stdout</summary>
    private void WriteSdkJson<T>(T message) {
        var json = RelaxedJsonSerializer.Serialize(message, _jsonContext);
        Console.WriteLine(json);
        if (Console.IsOutputRedirected) Console.Out.Flush();
    }
}