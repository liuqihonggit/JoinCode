namespace JoinCode.Adapters;

/// <summary>
/// 控制台表示层适配器 — 纯 CLI 模式，无 TUI 依赖
/// </summary>
public sealed class ConsolePresentationAdapter : IPresentationAdapter, IStreamingOutputWriter
{
    private readonly IConsoleOutput _output;
    private string _streamingContent = string.Empty;
    private bool _isDisposed;

    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; private set; }

    public ConsolePresentationAdapter(IConsoleOutput output)
    {
        _output = output;
    }

    /// <summary>启动表示层</summary>
    public void Start() => IsRunning = true;

    /// <summary>停止表示层</summary>
    public void Stop() => IsRunning = false;

    /// <summary>显示助手消息</summary>
    public void DisplayAssistantMessage(string content) => _output.WriteLine(content);

    /// <summary>显示系统消息</summary>
    public void DisplaySystemMessage(string content) => _output.WriteWarning(content);

    /// <summary>显示错误消息</summary>
    public void DisplayErrorMessage(string content) => _output.WriteError(content);

    /// <summary>更新流式消息内容</summary>
    public void UpdateStreamingMessage(string content) => _streamingContent = content;

    /// <summary>提交流式消息的最终内容</summary>
    public void CommitStreamingMessage(string finalContent)
    {
        _output.WriteLine(finalContent);
        _streamingContent = string.Empty;
    }

    /// <summary>如果流式消息不为空则提交</summary>
    public void CommitStreamingIfNotEmpty()
    {
        if (!string.IsNullOrEmpty(_streamingContent)) CommitStreamingMessage(_streamingContent);
    }

    /// <summary>开始新的对话轮次</summary>
    public void StartNewTurn() { }

    /// <summary>显示工具调用开始</summary>
    public void ShowToolStart(string toolName, string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            _output.WriteLine($"[Tool] {toolName}");
        else
        {
            var display = arguments.Length > 200 ? string.Concat(arguments.AsSpan(0, 200), "...") : arguments;
            _output.WriteLine($"[Tool] {toolName}({display})");
        }
    }

    /// <summary>显示工具调用结果</summary>
    public void ShowToolResult(string toolName, string? result, bool success)
    {
        var glyph = success ? "OK" : "FAIL";
        if (string.IsNullOrEmpty(result))
            _output.WriteLine($"[{glyph}] {toolName}");
        else
        {
            var lines = result.Split('\n');
            var displayCount = Math.Min(lines.Length, 10);
            _output.WriteLine($"[{glyph}] {toolName}");
            for (var i = 0; i < displayCount; i++)
                _output.WriteLine($"  {lines[i].TrimEnd('\r')}");
            if (lines.Length > 10)
                _output.WriteLine($"  ... ({lines.Length} lines)");
        }
    }

    /// <summary>显示工具调用进度</summary>
    public void ShowToolProgress(string toolName, string progressType, string progressMessage)
        => _output.WriteLine($"[...] {toolName}: {progressMessage}");

    /// <summary>显示权限对话框</summary>
    public void ShowPermissionDialog(string dialogContent, string toolName)
        => _output.WriteLine(dialogContent);

    /// <summary>请求用户输入</summary>
    public Task<string> RequestInputAsync(string? prompt, CancellationToken ct = default)
    {
        if (prompt is not null)
        {
            var result = _output.Prompt(prompt);
            return Task.FromResult(result ?? string.Empty);
        }
        return Task.FromResult(string.Empty);
    }

    /// <summary>请求用户确认</summary>
    public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
        => Task.FromResult(_output.Confirm(message));

    void IStreamingOutputWriter.Write(string text) { }
    void IStreamingOutputWriter.WriteLine(string text) { }

    /// <summary>标记完成</summary>
    public void MarkCompleted() { }

    /// <summary>标记失败</summary>
    public void MarkFailed(string errorMessage) { }

    public void Dispose()
    {
        if (_isDisposed) return;
        Stop();
        _isDisposed = true;
    }
}

/// <summary>
/// 流式输出写入器接口 — 简化版，无 TUI 依赖
/// </summary>
public interface IStreamingOutputWriter
{
    void Write(string text);
    void WriteLine(string text);
}
