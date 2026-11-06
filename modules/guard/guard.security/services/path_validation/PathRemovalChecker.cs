namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 危险删除路径检测器 — 从 PathConstraintValidator 拆分而来。
/// 职责：检测 rm/rmdir 是否针对系统关键目录（/, /etc, C:\ 等）。
/// 对齐 TS checkDangerousRemovalPaths + isDangerousRemovalPath。
/// </summary>
internal static class PathRemovalChecker {

    /// <summary>
    /// 预归一化危险路径 — Replace('\\','/')+TrimEnd('/') 在静态初始化时一次性计算,消除循环内重复分配
    /// P2-⑨ 源+派生缓存合并: 直接从路径列表构建,消除中间 FrozenSet 源字段
    /// </summary>
    internal static readonly string[] DangerousRemovalPathsNormalized = new string[]
        {
            "/", "/tmp", "/etc", "/usr", "/bin", "/sbin", "/var", "/root",
            "/home", "/opt", "/sys", "/proc", "/dev", "/lib",
            @"C:\", @"C:\Windows", @"C:\Program Files", @"C:\Users",
            @"D:\", @"E:\",
        }
        .Select(d => d.Replace('\\', '/').TrimEnd('/'))
        .ToArray();

    /// <summary>
    /// 检查是否为危险删除路径 — 对齐 TS isDangerousRemovalPath
    /// 预归一化危险路径 + stackalloc Span 归一化输入,零堆分配(原每次调用 60 次分配)
    /// </summary>
    internal static bool IsDangerousRemovalPath(string absolutePath) {
        if (string.IsNullOrEmpty(absolutePath)) {
            return false;
        }

        Span<char> normalized = stackalloc char[absolutePath.Length];
        var source = absolutePath.AsSpan();
        for (var i = 0; i < source.Length; i++)
            normalized[i] = source[i] == '\\' ? '/' : source[i];
        var normalizedSpan = normalized.TrimEnd('/');

        foreach (var dangerous in DangerousRemovalPathsNormalized) {
            var dangerousSpan = dangerous.AsSpan();
            if (normalizedSpan.Equals(dangerousSpan, StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalizedSpan.Length > dangerousSpan.Length
                && normalizedSpan.StartsWith(dangerousSpan, StringComparison.OrdinalIgnoreCase)
                && normalizedSpan[dangerousSpan.Length] == '/')
                return true;
        }
        return false;
    }
}
