namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 根查找中间件 — 查找 Git 仓库根目录
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeGitRootMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    /// <summary>
    /// 构造 WorktreeGitRootMiddleware 实例，注入文件操作服务、文件系统及日志器
    /// </summary>
    public WorktreeGitRootMiddleware(IFileOperationService fs, IFileSystem fileSystem, ILogger<WorktreeGitRootMiddleware>? logger = null)
    {
        _fs = fs;
        _fileSystem = fileSystem;
        _logger = logger;
    }
    private readonly IFileOperationService _fs;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<WorktreeGitRootMiddleware>? _logger;


    /// <summary>
    /// 执行 Git 根查找：若上下文已有 GitRoot 则跳过；否则从 GitRootPath 或当前目录向上查找 git 根目录
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(context.GitRoot))
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var gitRoot = context.GitRootPath ?? await GitWorkspaceResolver.FindGitRootAsync(_fileSystem.GetCurrentDirectory(), _fileSystem, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(gitRoot))
        {
            context.Fail("未找到 Git 仓库根目录");
            return;
        }

        context.GitRoot = gitRoot;
        if (string.IsNullOrEmpty(context.OriginalCwd))
        {
            context.OriginalCwd = _fs.GetCurrentDirectory();
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
