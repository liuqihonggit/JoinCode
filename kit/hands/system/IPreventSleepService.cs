
namespace Services.SystemPower;

/// <summary>
/// 防睡眠服务接口 — 提供阻止/恢复系统睡眠状态的能力
/// </summary>
public interface IPreventSleepService : IDisposable {
    /// <summary>
    /// 阻止系统进入睡眠状态
    /// </summary>
    /// <param name="type">防睡眠类型,默认为 Continuous(连续防睡眠)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功阻止返回 true;失败返回 false</returns>
    Task<bool> PreventSleepAsync(SleepPreventionType type = SleepPreventionType.Continuous, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复系统默认执行状态,允许系统进入睡眠
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功恢复返回 true;失败返回 false</returns>
    Task<bool> AllowSleepAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前是否已阻止系统睡眠
    /// </summary>
    bool IsSleepPrevented { get; }
}