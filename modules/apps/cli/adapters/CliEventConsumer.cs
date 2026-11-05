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
    private string? _sessionId;
    private bool _systemInitSent;
    private int _uuidCounter;

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
    public CliEventConsumer(bool agentMode, string? sessionId = null, System.Collections.Generic.IReadOnlyList<string>? tools = null, string? model = null, string? version = null) {
        _agentMode = agentMode;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
        _sessionId = sessionId;
        _tools = tools ?? [];
        _model = model;
        _version = version;
    }

    private readonly System.Collections.Generic.IReadOnlyList<string> _tools = [];
    private readonly string? _model;
    private readonly string? _version;

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
        _systemInitSent = false;
        _uuidCounter = 0;
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
        if (_agentMode) {
            EnsureSystemInit();
            _pendingBlocks.Add(new Cli.Output.SdkToolUseBlock {
                Id = toolCallId ?? string.Empty,
                Name = toolName,
                Input = arguments ?? "{}"
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
        if (_agentMode) {
            _pendingToolResults.Add(new Cli.Output.SdkToolResultBlock {
                ToolUseId = toolCallId ?? string.Empty,
                Content = resultText ?? string.Empty,
                IsError = isError ? true : null
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
            WriteSdkJson(new Cli.Output.SdkToolProgressMessage {
                ToolName = toolName,
                ToolUseId = string.Empty,
                ElapsedTimeSeconds = 0,
                Uuid = NextUuid(),
                SessionId = _sessionId
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

    /// <summary>确保 system/init 消息已发送（首次事件时发送）</summary>
    private void EnsureSystemInit() {
        if (_systemInitSent) return;
        _systemInitSent = true;
        _sessionId ??= System.Guid.NewGuid().ToString("N");
        WriteSdkJson(new Cli.Output.SdkSystemInitMessage {
            ClaudeCodeVersion = _version ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
            Cwd = System.IO.Directory.GetCurrentDirectory(),
            Model = _model ?? System.Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "unknown",
            Tools = _tools.Count > 0 ? new System.Collections.Generic.List<string>(_tools) : [],
            PermissionMode = System.Environment.GetEnvironmentVariable("JCC_PERMISSION_MODE") ?? "default",
            Uuid = NextUuid(),
            SessionId = _sessionId
        });
    }

    /// <summary>Flush 累积的 assistant 消息（如果有 content blocks），合并相邻同类型 block</summary>
    private void FlushAssistant() {
        if (_pendingBlocks.Count == 0) return;
        var merged = MergeAdjacentBlocks(_pendingBlocks);
        WriteSdkJson(new Cli.Output.SdkAssistantMessage {
            Message = new Cli.Output.SdkMessageBody {
                Role = "assistant",
                Content = merged
            },
            ParentToolUseId = null,
            Uuid = NextUuid(),
            SessionId = _sessionId
        });
        _pendingBlocks.Clear();
    }

    /// <summary>Flush 累积的 tool_result 作为单个 user 消息（对齐基准 — 多 tool_result 合并到同一 user 消息）</summary>
    private void FlushToolResults() {
        if (_pendingToolResults.Count == 0) return;
        WriteSdkJson(new Cli.Output.SdkUserMessage {
            Message = new Cli.Output.SdkMessageBody {
                Role = "user",
                Content = _pendingToolResults.Cast<Cli.Output.SdkContentBlock>().ToList()
            },
            Uuid = NextUuid(),
            SessionId = _sessionId
        });
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

    /// <summary>生成下一个 uuid</summary>
    private string NextUuid() => $"msg_{System.Threading.Interlocked.Increment(ref _uuidCounter):x8}";

    /// <summary>写入 SDK JSON 消息到 stdout</summary>
    private void WriteSdkJson<T>(T message) {
        var json = RelaxedJsonSerializer.Serialize(message, _jsonContext);
        Console.WriteLine(json);
        if (Console.IsOutputRedirected) Console.Out.Flush();
    }
}