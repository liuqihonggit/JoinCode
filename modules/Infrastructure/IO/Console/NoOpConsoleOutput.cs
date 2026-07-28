namespace Infrastructure.IO;

/// <summary>
/// 静默控制台输出 — 所有输出被丢弃，用于 E2E 测试和 CI
/// JCC_CONSOLE_MODE=NoOp 时激活
/// </summary>
public sealed class NoOpConsoleOutput : IConsoleOutput
{
    public void WriteLine(string message) { }
    public void WriteError(string message) { }
    public void WriteSuccess(string message) { }
    public void WriteWarning(string message) { }
    public string? Prompt(string message) => null;
    public bool Confirm(string message) => false;
    public void WriteLine(string message, ConsoleColor color) { }
    public string ReadPassword(string prompt) => string.Empty;
}
