namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 根查找中间件 — 查找 Git 仓库根目录
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeGitRootMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    public WorktreeGitRootMiddleware(IFileOperationService fs, IFileSystem fileSystem, ILogger<WorktreeGitRootMiddleware>? logger = null)
    {
        _fs = fs;
        _fileSystem = fileSystem;
        _logger = logger;
    }
    private readonly IFileOperationService _fs;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<WorktreeGitRootMiddleware>? _logger;


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
