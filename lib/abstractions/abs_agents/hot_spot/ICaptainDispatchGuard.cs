namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 队长派发守卫 — 派发前查热点表，热点文件契约改队长自己揽不派Worker
/// 纯逻辑决策，不执行实际派发（执行在单元C接入GoalGraphEngine时调用）
/// </summary>
public interface ICaptainDispatchGuard
{
    /// <summary>
    /// 派发前检查：任务涉及热点文件契约改 → 队长自己揽
    /// </summary>
    /// <param name="taskFiles">任务涉及的文件列表</param>
    /// <returns>派发决策（队长自己揽 or 派给Worker）</returns>
    DispatchDecision CheckBeforeDispatch(IReadOnlyList<string> taskFiles);
}
