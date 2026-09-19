
namespace Memdir.Sync;

/// <summary>
/// 同步冲突解决策略 — 当本地与远程文件发生冲突时指定保留哪一方或进行合并。
/// </summary>
public enum SyncConflictResolution {
    /// <summary>保留本地版本。</summary>
    [EnumValue("keepLocal")]
    KeepLocal,
    /// <summary>保留远程版本。</summary>
    [EnumValue("keepRemote")]
    KeepRemote,
    /// <summary>保留最新修改时间的一方。</summary>
    [EnumValue("keepNewest")]
    KeepNewest,
    /// <summary>合并本地与远程版本。</summary>
    [EnumValue("merge")]
    Merge
}