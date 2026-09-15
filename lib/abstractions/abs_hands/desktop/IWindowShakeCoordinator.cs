namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 窗口震动去抖协调器 — 进程内单例，1 秒去抖合并多个子代理的震动请求。
/// 防止同进程内多个子代理同时发出震动导致高频抖动。
/// </summary>
public interface IWindowShakeCoordinator
{
    /// <summary>
    /// 尝试获取震动时间槽。1 秒内只允许一次成功，后续请求被合并忽略。
    /// </summary>
    /// <returns>true 表示获取成功可执行震动；false 表示 1 秒内已震动过，应跳过。</returns>
    bool TryAcquireShakeSlot();
}
