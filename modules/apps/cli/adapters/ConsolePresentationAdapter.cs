namespace JoinCode.Adapters;

/// <summary>
/// 控制台表示层适配器 — 纯 CLI 模式，无 TUI 依赖
/// </summary>
public sealed class ConsolePresentationAdapter : PresentationAdapterBase, IStreamingOutputWriter {
    private readonly IConsoleOutput _output;
    private string _streamingContent = string.Empty;
    private int _isDisposed;

    /// <summary>是否正在运行</summary>
    public override bool IsRunning { get; protected set; }

    /// <summary>
    /// 构造函数 — 注入控制台输出接口
    /// </summary>
    /// <param name="output">控制台输出接口</param>
    public ConsolePresentationAdapter(IConsoleOutput output) {
        _output = output;
    }

    /// <inheritdoc />
    public override void Start() => IsRunning = true;

    /// <inheritdoc />
    public override void Stop() => IsRunning = false;

    /// <inheritdoc />
    public override void DisplayAssistantMessage(string content) => _output.WriteLine(content);

    /// <inheritdoc />
    public override void DisplaySystemMessage(string content) => _output.WriteWarning(content);

    /// <inheritdoc />
    public override void DisplayErrorMessage(string content) => _output.WriteError(content);

    /// <inheritdoc />
    public override void UpdateStreamingMessage(string content) => _streamingContent = content;

    /// <inheritdoc />
    public override void CommitStreamingMessage(string finalContent) {
        _output.WriteLine(finalContent);
        _streamingContent = string.Empty;
    }

    /// <inheritdoc />
    public override void CommitStreamingIfNotEmpty() {
        if (!string.IsNullOrEmpty(_streamingContent)) CommitStreamingMessage(_streamingContent);
    }

    /// <inheritdoc />
    public override void ShowToolStart(string toolName, string? arguments) {
        if (string.IsNullOrEmpty(arguments))
            _output.WriteLine($"[Tool] {toolName}");
        else {
            var display = arguments.Length > 200 ? string.Concat(arguments.AsSpan(0, 200), "...") : arguments;
            _output.WriteLine($"[Tool] {toolName}({display})");
        }
    }

    /// <inheritdoc />
    public override void ShowToolResult(string toolName, string? result, bool success) {
        var glyph = success ? "OK" : "FAIL";
        if (string.IsNullOrEmpty(result))
            _output.WriteLine($"[{glyph}] {toolName}");
        else {
            var lines = result.Split('\n');
            var displayCount = Math.Min(lines.Length, 10);
            _output.WriteLine($"[{glyph}] {toolName}");
            for (var i = 0; i < displayCount; i++)
                _output.WriteLine($"  {lines[i].TrimEnd('\r')}");
            if (lines.Length > 10)
                _output.WriteLine($"  ... ({lines.Length} lines)");
        }
    }

    /// <inheritdoc />
    public override void ShowToolProgress(string toolName, string progressType, string progressMessage)
        => _output.WriteLine($"[...] {toolName}: {progressMessage}");

    /// <inheritdoc />
    public override void ShowPermissionDialog(string dialogContent, string toolName)
        => _output.WriteLine(dialogContent);

    /// <inheritdoc />
    public override Task<string> RequestInputAsync(string? prompt, CancellationToken ct = default) {
        if (prompt is not null) {
            var result = _output.Prompt(prompt);
            return Task.FromResult(result ?? string.Empty);
        }
        return Task.FromResult(string.Empty);
    }

    /// <inheritdoc />
    public override Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
        => Task.FromResult(_output.Confirm(message));

    /// <summary>写入文本（流式输出空实现）</summary>
    void IStreamingOutputWriter.Write(string text) { }
    /// <summary>写入一行文本（流式输出空实现）</summary>
    void IStreamingOutputWriter.WriteLine(string text) { }

    /// <summary>标记完成</summary>
    public void MarkCompleted() { }

    /// <summary>标记失败</summary>
    public void MarkFailed(string errorMessage) { }

    /// <summary>释放资源 — 标记已释放并停止表示层</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
        Stop();
    }

    /// <summary>异步释放资源 — 委托给同步 Dispose</summary>
    public override ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 流式输出写入器接口 — 简化版，无 TUI 依赖
/// </summary>
public interface IStreamingOutputWriter {
    /// <summary>写入文本到输出流</summary>
    /// <param name="text">要写入的文本</param>
    void Write(string text);
    /// <summary>写入一行文本到输出流并换行</summary>
    /// <param name="text">要写入的文本</param>
    void WriteLine(string text);
}