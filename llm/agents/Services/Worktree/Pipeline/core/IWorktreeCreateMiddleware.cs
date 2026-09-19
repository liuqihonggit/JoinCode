namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 创建中间件标记接口 — 通过 Order 属性声明执行优先级,管道构建处按 Order 升序排列
/// </summary>
public interface IWorktreeCreateMiddleware : IMiddleware<WorktreeCreateContext>
{
    /// <summary>执行优先级(升序),数值越小越先执行。业务期望顺序: Validation(100)→GitRoot(200)→Recovery(300)→GitInfo(400)→Create(500)→Config(600)→SessionSave(700)</summary>
    int Order { get; }
}
