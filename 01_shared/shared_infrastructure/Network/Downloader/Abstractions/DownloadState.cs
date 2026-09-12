namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载会话状态枚举 — 驱动状态机(DownloadStateMachine,待 T2 创建)的状态转换
/// <para>终态: Completed/Cancelled/Failed,不可再转换</para>
/// <para>非终态: Idle/Downloading/Paused/Merging,可响应 Pause/Resume/Cancel</para>
/// </summary>
public enum DownloadState
{
    /// <summary>已创建,未启动</summary>
    Idle,

    /// <summary>下载中(分片并发进行中)</summary>
    Downloading,

    /// <summary>已暂停(进度已持久化到 .meta.json,可 Resume)</summary>
    Paused,

    /// <summary>分片合并中(所有 chunk 完成,正在拼接 .part 为目标文件)</summary>
    Merging,

    /// <summary>已完成(终态,目标文件就绪,临时文件已清理)</summary>
    Completed,

    /// <summary>已取消(终态,临时文件和 .meta.json 已清理)</summary>
    Cancelled,

    /// <summary>已失败(终态,保留 .meta.json 供诊断,临时文件保留供恢复)</summary>
    Failed
}
