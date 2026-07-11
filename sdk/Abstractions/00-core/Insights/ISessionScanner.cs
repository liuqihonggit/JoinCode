namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 洞察会话扫描器接口 — 扫描所有会话文件并提取洞察元数据
/// 对齐 TS insights.ts scanAllSessions + logToSessionMeta
/// </summary>
public interface IInsightSessionScanner
{
    /// <summary>
    /// 扫描所有会话，返回洞察元数据列表
    /// </summary>
    Task<IReadOnlyList<InsightSessionMeta>> ScanAllSessionsAsync(CancellationToken cancellationToken = default);
}
