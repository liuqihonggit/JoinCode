namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 参数验证中间件 — 检查 AgentId 有效性
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeValidationMiddleware : IWorktreeCreateMiddleware
{

    public Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var agentId = context.AgentId;

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent ID 不能为空", nameof(agentId));
        }

        if (agentId.Length > 64)
        {
            throw new ArgumentException("Agent ID 长度超过 64 字符限制", nameof(agentId));
        }

        if (agentId.Contains("..") || agentId.Contains('/') || agentId.Contains('\\') || agentId.Contains(':')
            || agentId.Contains('\0') || agentId.Any(char.IsControl))
        {
            throw new ArgumentException("Agent ID 包含非法字符", nameof(agentId));
        }

        return next(context, ct);
    }
}
