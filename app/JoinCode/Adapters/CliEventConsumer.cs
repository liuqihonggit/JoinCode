namespace JoinCode.Adapters;

/// <summary>
/// CLI 事件消费策略 — DX 模式使用 TerminalHelper 彩色输出，AX 模式输出 NDJSON 事件流
/// </summary>
public sealed class CliEventConsumer : IResettableEventConsumer
{
    private readonly StringBuilder _fullResponse = new();
    private readonly StringBuilder _thinkingContent = new();
    private bool _isFirstToken = true;
    private readonly bool _agentMode;
    private readonly Cli.Output.CliOutputJsonContext _jsonContext;

    public CliEventConsumer()
    {
        _agentMode = false;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
    }

    public CliEventConsumer(bool agentMode)
    {
        _agentMode = agentMode;
        _jsonContext = Cli.Output.CliOutputJsonContext.Default;
    }

    /// <summary>累积的完整响应文本</summary>
    public string FullResponse => _fullResponse.ToString();

    /// <summary>累积的思考内容</summary>
    public string ThinkingContent => _thinkingContent.ToString();

    /// <summary>是否已收到首个 token</summary>
    public bool IsFirstToken => _isFirstToken;

    /// <summary>重置状态（新一轮对话前调用）</summary>
    public void Reset()
    {
        _fullResponse.Clear();
        _thinkingContent.Clear();
        _isFirstToken = true;
    }

    /// <summary>收到文本内容</summary>
    public void OnText(string content)
    {
        if (_isFirstToken) _isFirstToken = false;
        if (content.Length > 0)
        {
            _fullResponse.Append(content);
            if (_agentMode)
            {
                WriteNdJsonEvent("text", new Cli.Output.CliStreamEventData { Content = content });
            }
            else
            {
                using var _ = TerminalHelper.SetColor(ConsoleColor.Cyan);
                TerminalHelper.WriteRaw(content);
            }
        }
    }

    /// <summary>收到思考内容</summary>
    public void OnThinking(string thinking)
    {
        if (thinking.Length > 0)
        {
            _thinkingContent.Append(thinking);
            if (_agentMode)
            {
                WriteNdJsonEvent("thinking", new Cli.Output.CliStreamEventData { Content = thinking });
            }
        }
    }

    /// <summary>工具调用开始</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("tool_start", new Cli.Output.CliStreamEventData { ToolName = toolName, ToolCallId = toolCallId, Arguments = arguments });
        }
        else
        {
            using var _ = TerminalHelper.SetColor(ConsoleColor.DarkGray);
            TerminalHelper.NewLine();
            if (string.IsNullOrEmpty(arguments))
                TerminalHelper.WriteLine($"[Tool] {toolName}");
            else
            {
                var display = arguments.Length > 200 ? string.Concat(arguments.AsSpan(0, 200), "...") : arguments;
                TerminalHelper.WriteLine($"[Tool] {toolName}({display})");
            }
        }
    }

    /// <summary>工具调用结束</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("tool_end", new Cli.Output.CliStreamEventData { ToolName = toolName, ToolCallId = toolCallId, IsError = isError, ResultLength = resultText?.Length ?? 0 });
        }
        else
        {
            using var _ = TerminalHelper.SetColor(isError ? ConsoleColor.Red : ConsoleColor.DarkGray);
            var glyph = isError ? "FAIL" : "OK";
            TerminalHelper.WriteLine($"[{glyph}] {toolName}");
            if (!string.IsNullOrEmpty(resultText) && isError)
            {
                var lines = resultText.Split('\n');
                var displayCount = Math.Min(lines.Length, 5);
                for (var i = 0; i < displayCount; i++)
                    TerminalHelper.WriteLine($"  {lines[i].TrimEnd('\r')}");
                if (lines.Length > 5)
                    TerminalHelper.WriteLine($"  ... ({lines.Length} lines)");
            }
        }
    }

    /// <summary>工具调用进度</summary>
    public void OnToolProgress(string toolName, string progressType, string? progressMessage)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("tool_progress", new Cli.Output.CliStreamEventData { ToolName = toolName, ProgressType = progressType, ProgressMessage = progressMessage });
        }
        else
        {
            TerminalHelper.WriteLine($"[...] {toolName}: {progressMessage}");
        }
    }

    /// <summary>检测到循环输出</summary>
    public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("loop_detected", new Cli.Output.CliStreamEventData { TriggerCount = triggerCount, LoopStartIndex = loopStartIndex });
        }
        else
        {
            TerminalHelper.WriteLine($"[Loop] 检测到循环输出(第{triggerCount}次)");
        }
    }

    /// <summary>计时摘要</summary>
    public void OnTimingSummary(string summary)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("timing", new Cli.Output.CliStreamEventData { Summary = summary });
        }
        else
        {
            TerminalHelper.WriteLine();
            TerminalHelper.WriteRaw(summary);
        }
    }

    /// <summary>流式响应完成</summary>
    public void OnDone(TokenUsage? usage, string? modelId)
    {
        if (_agentMode)
        {
            WriteNdJsonEvent("done", new Cli.Output.CliStreamEventData { Usage = usage, ModelId = modelId });
        }
    }

    /// <summary>
    /// 写入 NDJSON 事件 — AX 模式下每行一个 JSON 对象，输出到 stdout
    /// </summary>
    private void WriteNdJsonEvent(string eventType, Cli.Output.CliStreamEventData payload)
    {
        var evt = new Cli.Output.CliStreamEvent(eventType) { Data = payload };
        var json = System.Text.Json.JsonSerializer.Serialize(evt, _jsonContext.CliStreamEvent);
        Console.WriteLine(json);
        if (Console.IsOutputRedirected) Console.Out.Flush();
    }
}
