namespace Infrastructure.IO;

/// <summary>
/// 静默控制台输出 — 所有输出被丢弃，用于 E2E 测试和 CI
/// JCC_CONSOLE_MODE=NoOp 时激活
/// </summary>
public sealed class NoOpConsoleOutput : IConsoleOutput {
    /// <inheritdoc/>
    public void WriteLine(string message) { }
    /// <inheritdoc/>
    public void WriteError(string message) { }
    /// <inheritdoc/>
    public void WriteSuccess(string message) { }
    /// <inheritdoc/>
    public void WriteWarning(string message) { }
    /// <inheritdoc/>
    public string? Prompt(string message) => null;
    /// <inheritdoc/>
    public bool Confirm(string message) => false;
    /// <inheritdoc/>
    public void WriteLine(string message, ConsoleColor color) { }
    /// <inheritdoc/>
    public string ReadPassword(string prompt) => string.Empty;
}