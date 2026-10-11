namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 根查找中间件 — 查找当前 git 工作区目录(不解析到主仓库)
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeGitRootMiddleware : ServiceEntity, IWorktreeCreateMiddleware {

    /// <summary>
    /// 构造 WorktreeGitRootMiddleware 实例，注入文件操作服务、文件系统及日志器
    /// </summary>
    public WorktreeGitRootMiddleware(IFileOperationService fs, IFileSystem fileSystem, ILogger<WorktreeGitRootMiddleware>? logger = null) {
        _fs = fs;
        _fileSystem = fileSystem;
        _logger = logger;
    }
    private readonly IFileOperationService _fs;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<WorktreeGitRootMiddleware>? _logger;


    /// <summary>执行优先级:Git 根查找在验证之后</summary>
    public int Order => 200;

    /// <summary>
    /// 执行 Git 工作区查找：若上下文已有 GitRoot 则跳过；否则用 FindGitWorkspaceDir 查找当前工作区目录。
    /// <para>关键:用 FindGitWorkspaceDir 而非 FindGitRootAsync — 在 git worktree 中不解析到主仓库,</para>
    /// <para>而是在当前 worktree(如 w3)下创建子 worktree,确保子 worktree 代码与当前工作目录一致。</para>
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct) {
        if (!string.IsNullOrEmpty(context.GitRoot)) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var gitRoot = context.GitRootPath ?? GitWorkspaceResolver.FindGitWorkspaceDir(_fileSystem.GetCurrentDirectory(), _fileSystem);
        if (string.IsNullOrEmpty(gitRoot)) {
            context.Fail("未找到 Git 工作区目录");
            return;
        }

        context.GitRoot = gitRoot;
        if (string.IsNullOrEmpty(context.OriginalCwd)) {
            context.OriginalCwd = _fs.GetCurrentDirectory();
        }

        _logger?.LogInformation("GitRoot 解析为当前工作区: {GitRoot} (不解析到主仓库,确保子 worktree 基于当前代码)", gitRoot);

        await next(context, ct).ConfigureAwait(false);
    }
}