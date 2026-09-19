namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 上下文自给自足辅助器 — 让每个中间件都能在入口确保依赖字段已填充,实现顺序无关
/// <para>设计理由:源码生成器按类名字母排序注册 DI,与业务期望顺序错位。即使有 Order 属性排序(第一层防线),</para>
/// <para>仍需每个中间件自给自足(第二层防线),确保任意执行顺序下字段依赖不断裂。</para>
/// </summary>
public static class WorktreeContextEnricher
{
    /// <summary>
    /// 确保 context.GitRoot 已填充 — 若为空则用 OriginalCwd 或当前目录回退(对齐 WorktreeCreateMiddleware 的回退策略)
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="fs">文件操作服务(用于获取当前目录)</param>
    public static void EnsureGitRoot(WorktreeCreateContext context, IFileOperationService fs)
    {
        if (!string.IsNullOrEmpty(context.GitRoot)) return;

        context.GitRoot = !string.IsNullOrEmpty(context.OriginalCwd)
            ? context.OriginalCwd
            : fs.GetCurrentDirectory();

        if (string.IsNullOrEmpty(context.OriginalCwd))
        {
            context.OriginalCwd = fs.GetCurrentDirectory();
        }
    }

    /// <summary>
    /// 确保 context.WorktreePath 已填充 — 若为空且 GitRoot 已填充,则用 GenerateWorktreePath 生成
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    public static void EnsureWorktreePath(WorktreeCreateContext context)
    {
        if (!string.IsNullOrEmpty(context.WorktreePath)) return;
        if (string.IsNullOrEmpty(context.GitRoot)) return;

        context.WorktreePath = AgentWorktreeSession.GenerateWorktreePath(context.GitRoot, context.AgentId);
    }

    /// <summary>
    /// 确保 context.BranchName 已填充 — 若为空则用 GenerateBranchName 生成
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    public static void EnsureBranchName(WorktreeCreateContext context)
    {
        if (!string.IsNullOrEmpty(context.BranchName)) return;

        context.BranchName = AgentWorktreeSession.GenerateBranchName(context.AgentId);
    }

    /// <summary>
    /// 确保 GitRoot + WorktreePath + BranchName 全部已填充 — ConfigMiddleware 等依赖完整路径的中间件入口调用
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="fs">文件操作服务</param>
    public static void EnsureAllPaths(WorktreeCreateContext context, IFileOperationService fs)
    {
        EnsureGitRoot(context, fs);
        EnsureWorktreePath(context);
        EnsureBranchName(context);
    }
}
