namespace JoinCode.Gui;

/// <summary>
/// GUI 诊断日志（ADR 0145 Core/Host 拆分点）：写 <c>~/.jcc 或项目 .jcc/dumps/startup_timing.log</c>，
/// 实现零 UI（原定义在 Avalonia <c>App</c> 类上，协议层 34 处调用被迫跨层引用 — 迁入 Core 后消除）。
/// Host 的 <c>App.LogDiag</c> 委托本类，行为不变。
/// </summary>
public static class GuiDiag {
    /// <summary>写一条诊断日志（fire-and-forget，失败仅写标准错误流）。public 供 Host 的 App.LogDiag 委托（ADR 0145）。</summary>
    public static void Log(string message) => _ = LogAsync(message);

    private static async Task LogAsync(string message) {
        try {
            var dir = AppDataConstants.Paths.DumpsDirectory;
            Directory.CreateDirectory(dir);
            await SafeFileIO.AppendAllText(
                Path.Combine(dir, "startup_timing.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        } catch (Exception logEx) {
            Console.Error.WriteLine($"[diag] timing log failed: {logEx.Message}");
        }
    }
}
