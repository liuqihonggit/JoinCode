namespace JoinCode.Guard.Configuration.ConfigRoot;

/// <summary>
/// 项目根定位器实现 — 从当前工作目录向上搜索 .git/ 或 .jcc/ 定位项目根（TASK054）。
/// <para>搜索策略：从 CWD 开始逐层向上，每层检查 .git/ 目录或 .jcc/ 目录是否存在，取最近的一个。</para>
/// <para>找到 .git/ 或 .jcc/ → 返回 true + 该层目录路径（项目根）；到根目录还未找到 → 返回 false。</para>
/// <para>线程安全：无共享可变状态，IFileSystem 实例自身负责线程安全。</para>
/// </summary>
public sealed class ProjectLocator : IProjectLocator {
    private readonly IFileSystem _fs;
    private const string GitDirName = ".git";
    private const string JccDirName = ".jcc";

    /// <summary>
    /// 构造项目根定位器。
    /// </summary>
    /// <param name="fs">文件系统抽象（用于目录存在检查和路径导航）</param>
    public ProjectLocator(IFileSystem fs) {
        _fs = fs;
    }

    /// <inheritdoc />
    public bool TryLocateProjectRoot([NotNullWhen(true)] out string? projectRoot) {
        var current = _fs.GetCurrentDirectory();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrEmpty(current) && visited.Add(current)) {
            if (HasMarkerDir(current)) {
                projectRoot = current;
                return true;
            }

            var parent = _fs.GetParentPath(current);
            if (parent is null) {
                projectRoot = null;
                return false;
            }
            current = parent;
        }

        projectRoot = null;
        return false;
    }

    private bool HasMarkerDir(string dir) {
        var gitPath = _fs.CombinePath(dir, GitDirName);
        if (_fs.DirectoryExists(gitPath)) {
            return true;
        }

        var jccPath = _fs.CombinePath(dir, JccDirName);
        return _fs.DirectoryExists(jccPath);
    }
}
