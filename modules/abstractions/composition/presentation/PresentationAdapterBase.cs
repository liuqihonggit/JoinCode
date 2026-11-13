namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 表示层适配器抽象基类 — 提供所有方法的默认空实现/no-op。
/// 子类只需选择性覆写需要的方法，无需实现全部 15+ 接口方法。
/// 类似 DSH LlmAdapter 的"抽象类 + 默认实现 + 选择性覆写"模式。
/// </summary>
public abstract class PresentationAdapterBase : IPresentationAdapter {
    /// <inheritdoc />
    public virtual bool IsRunning { get; protected set; }

    /// <inheritdoc />
    public virtual void Start() => IsRunning = true;

    /// <inheritdoc />
    public virtual void Stop() => IsRunning = false;

    /// <inheritdoc />
    public virtual void DisplayAssistantMessage(string content) { }

    /// <inheritdoc />
    public virtual void DisplaySystemMessage(string content) { }

    /// <inheritdoc />
    public virtual void DisplayErrorMessage(string content) { }

    /// <inheritdoc />
    public virtual void UpdateStreamingMessage(string content) { }

    /// <inheritdoc />
    public virtual void CommitStreamingMessage(string finalContent) { }

    /// <inheritdoc />
    public virtual void CommitStreamingIfNotEmpty() { }

    /// <inheritdoc />
    public virtual void StartNewTurn() { }

    /// <inheritdoc />
    public virtual void ShowToolStart(string toolName, string? arguments) { }

    /// <inheritdoc />
    public virtual void ShowToolResult(string toolName, string? result, bool success) { }

    /// <inheritdoc />
    public virtual void ShowToolProgress(string toolName, string progressType, string progressMessage) { }

    /// <inheritdoc />
    public virtual void ShowPermissionDialog(string dialogContent, string toolName) { }

    /// <inheritdoc />
    public virtual Task<string> RequestInputAsync(string? prompt = null, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    /// <inheritdoc />
    public virtual Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
