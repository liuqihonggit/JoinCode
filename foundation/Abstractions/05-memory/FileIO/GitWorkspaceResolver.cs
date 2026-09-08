namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Git 工作区寻址工具 — 统一解决 git root / solution root / workspace root 的向上搜索。
/// 支持 git worktree（.git 是文件时解析到主仓库）。
/// 替代项目中 11 处重复的 FindGitRoot / DiscoverWorkspaceRoot / FindWorkspaceRoot 实现。
/// </summary>
public static class GitWorkspaceResolver
{
    private static readonly string WorktreesMarker =
        System.IO.Path.DirectorySeparatorChar + ".git" + System.IO.Path.DirectorySeparatorChar + "worktrees" + System.IO.Path.DirectorySeparatorChar;

    /// <summary>
    /// 从 startPath 向上搜索 git 仓库根目录。
    /// .git 是目录 → 主仓库，直接返回当前路径。
    /// .git 是文件 → worktree，解析 gitdir 内容定位主仓库根。
    /// </summary>
    /// <param name="startPath">起始路径（文件或目录）</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>git 仓库根目录绝对路径，找不到返回 null</returns>
    public static async Task<string?> FindGitRootAsync(
        string startPath,
        IFileSystem fs,
        CancellationToken cancellationToken = default)
    {
        var currentPath = ResolveStartDirectory(startPath, fs);
        if (string.IsNullOrEmpty(currentPath)) return null;

        while (!string.IsNullOrEmpty(currentPath))
        {
            var gitPath = fs.CombinePath(currentPath, ".git");
            if (fs.DirectoryExists(gitPath))
            {
                return currentPath;
            }

            if (fs.FileExists(gitPath))
            {
                var canonicalRoot = await ResolveCanonicalGitRootFromGitdirFileAsync(gitPath, currentPath, fs, cancellationToken).ConfigureAwait(false);
                return canonicalRoot ?? currentPath;
            }

            var parentPath = fs.GetParentPath(currentPath);
            if (string.IsNullOrEmpty(parentPath) || parentPath == currentPath) break;
            currentPath = parentPath;
        }

        return null;
    }

    /// <summary>
    /// 从 startPath 向上搜索解决方案根目录（.sln/.slnx 所在目录）。
    /// </summary>
    /// <param name="startPath">起始路径（文件或目录）</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>.sln/.slnx 所在目录绝对路径，找不到返回 null</returns>
    public static string? FindSolutionRoot(string startPath, IFileSystem fs)
    {
        var currentPath = ResolveStartDirectory(startPath, fs);
        if (string.IsNullOrEmpty(currentPath)) return null;

        while (!string.IsNullOrEmpty(currentPath))
        {
            if (fs.GetFiles(currentPath, "*.sln", SearchOption.TopDirectoryOnly).Length > 0 ||
                fs.GetFiles(currentPath, "*.slnx", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return currentPath;
            }

            var parentPath = fs.GetParentPath(currentPath);
            if (string.IsNullOrEmpty(parentPath) || parentPath == currentPath) break;
            currentPath = parentPath;
        }

        return null;
    }

    /// <summary>
    /// 从 startPath 向上搜索工作区根目录。
    /// 优先 .sln/.slnx（解决方案根），找不到则用 git root（支持 worktree 解析到主仓库）。
    /// </summary>
    /// <param name="startPath">起始路径（文件或目录），为 null/空时用当前工作目录</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工作区根目录绝对路径，找不到返回 null</returns>
    public static async Task<string?> FindWorkspaceRootAsync(
        string? startPath,
        IFileSystem fs,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveStartDirectory(startPath, fs);
        if (string.IsNullOrEmpty(path)) return null;

        var solutionRoot = FindSolutionRoot(path, fs);
        if (solutionRoot is not null) return solutionRoot;

        return await FindGitRootAsync(path, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 .git gitdir 文件内容，定位主仓库根目录。
    /// gitdir 文件格式: "gitdir: /path/to/main/.git/worktrees/w1"
    /// </summary>
    private static async Task<string?> ResolveCanonicalGitRootFromGitdirFileAsync(
        string gitdirFilePath,
        string worktreePath,
        IFileSystem fs,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = (await fs.ReadAllTextAsync(gitdirFilePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (!content.StartsWith("gitdir: ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var gitdirRelative = content["gitdir: ".Length..].Trim();
            var gitdirAbs = fs.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = System.IO.Path.GetFullPath(gitdirAbs);

            var markerIdx = normalizedGitdir.IndexOf(WorktreesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0) return null;

            var canonicalRoot = normalizedGitdir[..markerIdx];
            var canonicalGitDir = fs.CombinePath(canonicalRoot, ".git");
            return fs.DirectoryExists(canonicalGitDir) ? canonicalRoot : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 将 startPath 解析为目录路径：文件取所在目录，目录直接用，null/空取当前工作目录。
    /// </summary>
    private static string? ResolveStartDirectory(string? startPath, IFileSystem fs)
    {
        if (string.IsNullOrEmpty(startPath))
        {
            try { return fs.GetCurrentDirectory(); }
            catch { return null; }
        }

        if (fs.FileExists(startPath))
        {
            return System.IO.Path.GetDirectoryName(startPath);
        }

        return fs.DirectoryExists(startPath) ? startPath : System.IO.Path.GetDirectoryName(startPath);
    }
}
