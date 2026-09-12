namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 调试日志缓冲区接口 — 捕获 Diag 诊断输出，供 /debug 命令查询
/// </summary>
public interface IDebugLogBuffer
{
    /// <summary>
    /// 当前缓冲区条目数
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 获取最近的日志条目
    /// </summary>
    IReadOnlyList<DebugLogEntry> GetRecent(int count = 100);

    /// <summary>
    /// 按级别过滤日志条目
    /// </summary>
    IReadOnlyList<DebugLogEntry> GetByLevel(DebugLogLevel level, int count = 100);

    /// <summary>
    /// 按最低级别过滤（包含该级别及以上）
    /// </summary>
    IReadOnlyList<DebugLogEntry> GetByMinLevel(DebugLogLevel minLevel, int count = 100);

    /// <summary>
    /// 清空缓冲区
    /// </summary>
    void Clear();
}
