namespace JoinCode.Abstractions.Security.Sandbox;

/// <summary>
/// 沙箱能力判定扩展 — 统一「真隔离」口径（文件系统 + 网络同时隔离），
/// 供门控、权限免确认、启动期 failIfUnavailable 共用，避免三处各自定义。
/// </summary>
public static class SandboxManagerCapabilityExtensions {
    /// <summary>
    /// 能力位是否构成真隔离 — 只有路径重定向 / 进程隔离（Windows Process/Soft）不算。
    /// </summary>
    /// <param name="capabilities">提供器能力位。</param>
    /// <returns>同时具备文件系统与网络隔离返回 true。</returns>
    public static bool HasRealIsolation(this SandboxCapabilities capabilities)
        => (capabilities & SandboxCapabilities.FileSystemIsolation) != 0
           && (capabilities & SandboxCapabilities.NetworkIsolation) != 0;

    /// <summary>
    /// 是否存在至少一个真隔离提供器（可用类型中逐个查能力位）。
    /// </summary>
    /// <param name="manager">沙箱管理器（可空）。</param>
    /// <returns>存在返回 true。</returns>
    public static bool HasRealIsolationProvider(this ISandboxManager? manager) {
        if (manager is null) {
            return false;
        }

        var types = manager.GetAvailableTypes();
        for (var i = 0; i < types.Length; i++) {
            // 提供器由管理器持有，本方法只读能力位，不接管所有权（分析器 JCC9305 因此要求内联访问）。
            if (manager.GetProvider(types[i])?.Capabilities.HasRealIsolation() == true) {
                return true;
            }
        }

        return false;
    }
}
