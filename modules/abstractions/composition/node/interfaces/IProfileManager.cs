namespace JoinCode.Abstractions.Composition;

/// <summary>
/// Profile 运行期管理接口 — 支持运行期 Profile 动态切换
/// 通过 volatile 引用原子替换实现无锁切换
/// </summary>
public interface IProfileManager {
    /// <summary>当前活跃的 ResolvedProfile（volatile 读）</summary>
    ResolvedProfile Current { get; }

    /// <summary>切换到指定 Profile（原子替换引用）</summary>
    /// <param name="profileName">目标 Profile 名称（如 base/headless/sdk/ci）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SwitchAsync(string profileName, CancellationToken cancellationToken = default);

    /// <summary>Profile 切换前事件 — 允许消费方做清理/准备（旧Profile → 新Profile）</summary>
    event Func<ResolvedProfile, ResolvedProfile, ValueTask>? ProfileSwitching;
}
