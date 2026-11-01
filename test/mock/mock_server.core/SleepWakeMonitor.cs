namespace MockServer.Core;

/// <summary>
/// 睡眠/唤醒监控 — 通用能力，统一注入所有后台进程。
/// Windows 上监听 SystemEvents.PowerModeChanged.Resume，非 Windows 无操作。
/// </summary>
public static class SleepWakeMonitor {
    /// <summary>
    /// 注册系统唤醒回调 — 电脑从睡眠恢复时触发。
    /// 多次调用注册多个回调，全部在唤醒时执行。
    /// </summary>
    /// <param name="callback">唤醒回调（异常被捕获不抛出，避免影响其他回调）</param>
    public static void OnResume(Action callback) {
        if (!OperatingSystem.IsWindows()) return;
        RegisterPlatformHandler(callback);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RegisterPlatformHandler(Action callback) {
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) {
                try { callback(); } catch (Exception ex) {
                    Console.Error.WriteLine($"[SleepWakeMonitor] 唤醒回调异常: {ex.Message}");
                }
            }
        };
    }
}
