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

    public WorktreeDecisionPolicy(
        int todoThreshold = 3,
        int hotFileThreshold = 1,
        int parallelismThreshold = 2)
    {
        _todoThreshold = todoThreshold;
        _hotFileThreshold = hotFileThreshold;
        _parallelismThreshold = parallelismThreshold;
    }

    public bool ShouldEnableWorktree(int todoCount, int hotFileCount, int estimatedParallelism)
    {
        if (todoCount >= _todoThreshold) return true;
        if (hotFileCount >= _hotFileThreshold) return true;
        if (estimatedParallelism >= _parallelismThreshold) return true;
        return false;
    }

    public AgentIsolationMode Decide(bool enableWorktree, ExecutorVariant variant)
    {
        if (!enableWorktree)
            return AgentIsolationMode.None;

        if (WorktreeEligibleVariants.Contains(variant.ToValue()))
            return AgentIsolationMode.Worktree;

        return AgentIsolationMode.None;
    }
}
