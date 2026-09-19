namespace JoinCode.Gui.ViewModels;

/// <summary>ViewModel 诊断日志 — 写入 dumps/ 目录，定位持久化/发送问题</summary>
internal static class ViewModelDiagnosticsLogger {
    /// <summary>写诊断日志到 dumps/persist_debug.log（定位持久化路由问题）</summary>
    public static void WriteDebug(string message) {
        try {
            var dir = AppDataConstants.Paths.DumpsDirectory;
            System.IO.Directory.CreateDirectory(dir);
            SafeFileIO.AppendAllText(
                System.IO.Path.Combine(dir, "persist_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        } catch (Exception writeEx) {
            System.Console.Error.WriteLine($"无法写入诊断日志: {writeEx.Message}");
        }
    }

    /// <summary>把发送异常写入 dumps/send_error.log 以便诊断；写入失败则忽略</summary>
    public static void WriteError(Exception ex) {
        try {
            var dir = AppDataConstants.Paths.DumpsDirectory;
            System.IO.Directory.CreateDirectory(dir);
            SafeFileIO.AppendAllText(
                System.IO.Path.Combine(dir, "send_error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {ex}{Environment.NewLine}");
        } catch (Exception writeEx) {
            System.Console.Error.WriteLine($"无法写入错误日志: {writeEx.Message}");
        }
    }
}