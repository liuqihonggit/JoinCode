
namespace JoinCode.Dream;

/// <summary>
/// 做梦回合记录
/// </summary>
public sealed record DreamTurn
{
    public required string Text { get; init; }
    public required int ToolUseCount { get; init; }
}

/// <summary>
/// 做梦任务状态 - 用于后台任务追踪
/// </summary>
public sealed class DreamTaskState
{
    // 基础任务字段
    public required string Id { get; init; }
    public TaskType Type => TaskType.Dream;
    public DreamTaskStatus Status { get; set; } = DreamTaskStatus.Running;
    public required string Description { get; init; }
    public required DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public bool Notified { get; set; }

    // 做梦特有字段
    public DreamPhase Phase { get; set; } = DreamPhase.Starting;
    public int SessionsReviewing { get; init; }

    /// <summary>
    /// 被修改的文件路径列表
    /// </summary>
    public List<string> FilesTouched { get; } = new();
    private HashSet<string> FilesTouchedSet { get; } = new();

    /// <summary>
    /// 助手回复记录（工具使用已折叠为计数）
    /// </summary>
    public List<DreamTurn> Turns { get; } = new();

    /// <summary>
    /// 取消控制器
    /// </summary>
    public CancellationTokenSource? AbortController { get; init; }

    /// <summary>
    /// 锁的原始mtime，用于失败时回滚
    /// </summary>
    public long PriorMtime { get; init; }

    /// <summary>
    /// 最大保留回合数
    /// </summary>
    private const int MaxTurns = WorkflowConstants.Dream.MaxTurns;

    /// <summary>
    /// 添加新的回合记录
    /// </summary>
    public void AddTurn(DreamTurn turn, IReadOnlyList<string> touchedPaths)
    {
        // 更新阶段：如果有文件被修改，进入updating阶段
        if (touchedPaths.Count > 0)
        {
            Phase = DreamPhase.Updating;

            // 添加新文件路径（去重）
            foreach (var path in touchedPaths)
            {
                if (!FilesTouchedSet.Contains(path))
                {
                    FilesTouched.Add(path);
                    FilesTouchedSet.Add(path);
                }
            }
        }

        // 添加回合（限制数量）
        if (Turns.Count >= MaxTurns)
        {
            Turns.RemoveAt(0);
        }
        Turns.Add(turn);
    }

    /// <summary>
    /// 完成任务
    /// </summary>
    public void Complete()
    {
        Status = DreamTaskStatus.Completed;
        EndTime = DateTime.UtcNow;
        Notified = true;
    }

    /// <summary>
    /// 标记失败
    /// </summary>
    public void Fail()
    {
        Status = DreamTaskStatus.Failed;
        EndTime = DateTime.UtcNow;
        Notified = true;
    }

    /// <summary>
    /// 标记为已杀死
    /// </summary>
    public void Kill()
    {
        Status = DreamTaskStatus.Killed;
        EndTime = DateTime.UtcNow;
        Notified = true;
        AbortController?.Cancel();
    }

    /// <summary>
    /// 检查是否为终态
    /// </summary>
    public bool IsTerminal => Status is DreamTaskStatus.Completed or DreamTaskStatus.Failed or DreamTaskStatus.Killed;
}

/// <summary>
/// 做梦任务状态守卫
/// </summary>
public static class DreamTaskGuards
{
    public static bool IsDreamTask(object? task) =>
        task is DreamTaskState;
}
