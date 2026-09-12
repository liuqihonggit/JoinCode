namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热文件监控兜底 — 发现 Worker 私自改热文件未上报时告警
/// 仅兜底纠错，不增加认领计数（PRD：上报制而非磁盘触发）
/// </summary>
public interface IHotFileWatchdog
{
    /// <summary>
    /// 检查文件变更：热文件被改但未上报意图 → 返回告警
    /// </summary>
    /// <param name="filePath">被改的文件路径</param>
    /// <param name="changerId">修改者 ID</param>
    /// <returns>告警信息（需要告警时），null=无需告警</returns>
    HotFileAlert? CheckChange(string filePath, string changerId);

    /// <summary>
    /// 批量检查文件变更
    /// </summary>
    /// <param name="changes">文件变更列表（路径, 修改者ID）</param>
    /// <returns>告警列表（空=全部正常）</returns>
    IReadOnlyList<HotFileAlert> CheckChanges(IReadOnlyList<(string FilePath, string ChangerId)> changes);
}
