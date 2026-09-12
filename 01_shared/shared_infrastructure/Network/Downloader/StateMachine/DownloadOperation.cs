namespace Infrastructure.Network.Downloader.StateMachine;

/// <summary>
/// 下载操作枚举 — 状态机可响应的操作类型,驱动 TryTransition 统一入口
/// <para>枚举是唯一数据源,禁止消费方硬编码操作字符串</para>
/// </summary>
public enum DownloadOperation
{
    /// <summary>启动:Idle → Downloading</summary>
    [EnumValue("start")] Start,

    /// <summary>暂停:Downloading → Paused</summary>
    [EnumValue("pause")] Pause,

    /// <summary>继续:Paused → Downloading</summary>
    [EnumValue("resume")] Resume,

    /// <summary>进入合并:Downloading → Merging</summary>
    [EnumValue("enter_merging")] EnterMerging,

    /// <summary>完成合并:Merging → Completed</summary>
    [EnumValue("complete")] Complete,

    /// <summary>取消:任意非终态 → Cancelled</summary>
    [EnumValue("cancel")] Cancel,

    /// <summary>失败:Downloading/Paused/Merging → Failed</summary>
    [EnumValue("fail")] Fail
}
