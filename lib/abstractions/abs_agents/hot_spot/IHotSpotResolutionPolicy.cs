namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热点处置策略 — 给定热点文件，决定队长接管+通知哪些Worker
/// 纯逻辑决策，不执行实际通知/接管（执行由中间件在单元C接入）
/// </summary>
public interface IHotSpotResolutionPolicy
{
    /// <summary>
    /// 对单个文件生成处置决策
    /// </summary>
    HotSpotResolution Resolve(string filePath);

    /// <summary>
    /// 对所有热点文件批量生成处置决策
    /// </summary>
    IReadOnlyList<HotSpotResolution> ResolveAll();
}
