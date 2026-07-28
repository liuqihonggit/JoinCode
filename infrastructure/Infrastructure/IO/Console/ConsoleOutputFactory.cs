namespace Infrastructure.IO;

/// <summary>
/// 控制台输出工厂 — 根据 JCC_CONSOLE_MODE 环境变量创建对应的 IConsoleOutput 实例
/// </summary>
public static class ConsoleOutputFactory
{
    /// <summary>
    /// 根据环境变量创建 IConsoleOutput 实例。
    /// JCC_CONSOLE_MODE=NoOp → NoOpConsoleOutput（静默所有输出）
    /// 其他/未设置 → PhysicalConsoleOutput（真实控制台，默认）
    /// </summary>
    public static IConsoleOutput Create()
    {
        var mode = EnvHelper.Get(JccEnvVar.ConsoleMode);
        if (string.Equals(mode, "NoOp", StringComparison.OrdinalIgnoreCase))
            return new NoOpConsoleOutput();
        return new PhysicalConsoleOutput();
    }
}
