namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热点追踪器 — 基于 IntentCollector 数据 + IHotFileDetector 判断哪些文件触发热点
/// 热文件 contract_claim>=1 即归队长；非热文件 contract_claim>=阈值才触发
/// internal_claim 不触发热点；队长修改不计入认领集合
/// </summary>
public interface IHotSpotTracker
{
    /// <summary>
    /// 判断某文件是否触发热点
    /// </summary>
    bool IsHotSpot(string filePath);

    /// <summary>
    /// 获取所有热点文件（触发热点的文件列表）
    /// </summary>
    IReadOnlyList<string> GetHotSpotFiles();

    /// <summary>
    /// 获取某文件的热点详细信息
    /// </summary>
    HotSpotInfo GetHotSpotInfo(string filePath);

    /// <summary>
    /// 配置阈值（热文件阈值默认1，非热文件阈值默认3）
    /// </summary>
    void SetThresholds(int hotFileThreshold, int normalFileThreshold);

    /// <summary>
    /// 会话结束清空所有统计
    /// </summary>
    void Clear();
}
