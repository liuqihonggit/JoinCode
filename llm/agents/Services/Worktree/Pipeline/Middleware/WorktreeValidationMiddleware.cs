namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 参数验证中间件 — 检查 AgentId 有效性
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeValidationMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    /// <summary>
    /// 执行参数验证：检查 AgentId 非空、长度不超限、不含非法字符（路径遍历/分隔符/控制字符）
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var agentId = context.AgentId;

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("[AGT017] Agent ID 不能为空", nameof(agentId));
        }

        if (agentId.Length > 64)
        {
            throw new ArgumentException("[AGT018] Agent ID 长度超过 64 字符限制", nameof(agentId));
        }

        if (agentId.Contains("..") || agentId.Contains('/') || agentId.Contains('\\') || agentId.Contains(':')
            || agentId.Contains('\0') || agentId.Any(char.IsControl))
        {
            throw new ArgumentException("[AGT019] Agent ID 包含非法字符", nameof(agentId));
        }

        return next(context, ct);
    }
}
