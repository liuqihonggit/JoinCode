namespace JoinCode.Adapters;

/// <summary>
/// CLI 事件消费策略 — 使用 TerminalHelper 输出
/// </summary>
public sealed class CliEventConsumer : IResettableEventConsumer
{
    private readonly StringBuilder _fullResponse = new();
    private readonly StringBuilder _thinkingContent = new();
    private bool _isFirstToken = true;

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
            TerminalHelper.WriteRaw(content);
        }
    }

    /// <summary>收到思考内容</summary>
    public void OnThinking(string thinking)
    {
        if (thinking.Length > 0) _thinkingContent.Append(thinking);
    }

    /// <summary>工具调用开始</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments)
    {
        TerminalHelper.NewLine();
        if (string.IsNullOrEmpty(arguments))
            TerminalHelper.WriteLine($"[Tool] {toolName}");
        else
        {
            var display = arguments.Length > 200 ? string.Concat(arguments.AsSpan(0, 200), "...") : arguments;
            TerminalHelper.WriteLine($"[Tool] {toolName}({display})");
        }
    }

    /// <summary>工具调用结束</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch)
    {
        var glyph = isError ? "FAIL" : "OK";
        TerminalHelper.WriteLine($"[{glyph}] {toolName}");
    }

    /// <summary>工具调用进度</summary>
    public void OnToolProgress(string toolName, string progressType, string? progressMessage)
    {
        TerminalHelper.WriteLine($"[...] {toolName}: {progressMessage}");
    }

    /// <summary>检测到循环输出</summary>
    public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern)
    {
        TerminalHelper.WriteLine($"[Loop] 检测到循环输出(第{triggerCount}次)");
    }

    /// <summary>计时摘要</summary>
    public void OnTimingSummary(string summary)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteRaw(summary);
    }

    /// <summary>流式响应完成</summary>
    public void OnDone(TokenUsage? usage, string? modelId)
    {
    }
}
