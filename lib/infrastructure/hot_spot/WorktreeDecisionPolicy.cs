namespace Infrastructure.HotSpot;

/// <summary>
/// worktree 决策策略实现 — 两层决策
/// 第一层：TODO>=3 或涉及热文件>=1 或并行度>=2 才开 worktree
/// 第二层：全局开 + Variant==Code 才开，Explore/Plan/Search 等只读不开
/// </summary>
[Register(typeof(IWorktreeDecisionPolicy), ServiceLifetime.Singleton)]
public sealed class WorktreeDecisionPolicy : IWorktreeDecisionPolicy
{
    private readonly int _todoThreshold;
    private readonly int _hotFileThreshold;
    private readonly int _parallelismThreshold;

    private static readonly FrozenSet<string> WorktreeEligibleVariants = FrozenSet.Create(
        StringComparer.Ordinal,
        ExecutorVariant.Code.ToValue(),
        ExecutorVariant.Verification.ToValue(),
        ExecutorVariant.Teammate.ToValue());

    /// <summary>
    /// 构造 worktree 决策策略
    /// </summary>
    /// <param name="todoThreshold">TODO 数量阈值,默认 3</param>
    /// <param name="hotFileThreshold">热文件数量阈值,默认 1</param>
    /// <param name="parallelismThreshold">并行度阈值,默认 2</param>
    public WorktreeDecisionPolicy(
        int todoThreshold = 3,
        int hotFileThreshold = 1,
        int parallelismThreshold = 2)
    {
        _todoThreshold = todoThreshold;
        _hotFileThreshold = hotFileThreshold;
        _parallelismThreshold = parallelismThreshold;
    }

    /// <summary>
    /// 判断是否应启用 worktree — TODO、热文件或并行度任一达阈值即启用
    /// </summary>
    /// <param name="todoCount">当前 TODO 数量</param>
    /// <param name="hotFileCount">涉及热文件数量</param>
    /// <param name="estimatedParallelism">预估并行度</param>
    /// <returns>启用返回 true,否则 false</returns>
    public bool ShouldEnableWorktree(int todoCount, int hotFileCount, int estimatedParallelism)
    {
        if (todoCount >= _todoThreshold) return true;
        if (hotFileCount >= _hotFileThreshold) return true;
        if (estimatedParallelism >= _parallelismThreshold) return true;
        return false;
    }

    /// <summary>
    /// 决策隔离模式 — 全局开关 + 执行器变体双重判定
    /// </summary>
    /// <param name="enableWorktree">全局是否启用 worktree</param>
    /// <param name="variant">执行器变体,仅 Code/Verification/Teammate 适用 worktree</param>
    /// <returns>隔离模式,启用且变体符合返回 Worktree,否则 None</returns>
    public AgentIsolationMode Decide(bool enableWorktree, ExecutorVariant variant)
    {
        if (!enableWorktree)
            return AgentIsolationMode.None;

        if (WorktreeEligibleVariants.Contains(variant.ToValue()))
            return AgentIsolationMode.Worktree;

        return AgentIsolationMode.None;
    }
}
