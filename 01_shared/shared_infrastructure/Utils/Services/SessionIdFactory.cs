namespace Core.Utils;

/// <summary>
/// 会话 ID 工厂（T10）— 统一五段式格式，消除 "default" 字面量兜底：
/// {yyyyMMdd-HHmm}-{项目名}-{当前分支}-parent-{ObjectId全局递增数}
/// 序号来自 ObjectId 原子自增（Interlocked.Increment，进程内全局唯一），
/// 项目名/分支名进程内缓存（git 调用仅首次）。
/// </summary>
public static class SessionIdFactory
{
    private static readonly Lazy<string> _defaultSessionId = new(() => CreateParent());
    private static readonly Lazy<(string Project, string Branch)> _cachedLocation = new(DetectLocation);

    /// <summary>
    /// 进程主会话 ID — 首次访问时生成一次并缓存。
    /// 所有历史 "default" 兜底点统一引用此值：无显式会话的组件共享同一真实 ID，
    /// 磁盘不再出现 default/ 目录。
    /// </summary>
    public static string DefaultSessionId => _defaultSessionId.Value;

    /// <summary>
    /// 新建父会话 ID — 每次调用产生新序号（同分钟多次生成靠 ObjectId 序号区分）。
    /// </summary>
    public static string CreateParent(string? workingDirectory = null, DateTime? createdAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        var (project, branch) = workingDirectory is null
            ? _cachedLocation.Value
            : DetectLocation(workingDirectory);

        var sequence = new JoinCode.Abstractions.Entity.ObjectId(JoinCode.Abstractions.Entity.ObjectType.Session).SequenceId;
        return $"{now:yyyyMMdd-HHmm}-{project}-{branch}-parent-{sequence}";
    }

    /// <summary>
    /// 新建 Fork 会话 ID — 派生式 {parentSessionId}-fork-{ObjectId全局递增数}
    /// 保持与主会话五段式格式一致的目录命名，消除 GUID 混入 sessions/ 目录
    /// </summary>
    public static string CreateFork(string parentSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSessionId);
        var sequence = new JoinCode.Abstractions.Entity.ObjectId(JoinCode.Abstractions.Entity.ObjectType.Session).SequenceId;
        return $"{parentSessionId}-fork-{sequence}";
    }

    /// <summary>
    /// 新建子代理 ID — 派生式 {parentSessionId}-sub-{ObjectId全局递增数}
    /// 统一子代理 ID 生成入口，消除散落的 GUID 回退
    /// </summary>
    public static string CreateSubAgent(string parentSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSessionId);
        var sequence = new JoinCode.Abstractions.Entity.ObjectId(JoinCode.Abstractions.Entity.ObjectType.Session).SequenceId;
        return $"{parentSessionId}-sub-{sequence}";
    }

    private static (string Project, string Branch) DetectLocation() => DetectLocation(Environment.CurrentDirectory);

    private static (string Project, string Branch) DetectLocation(string directory)
    {
        var projectName = SanitizeForPath(Path.GetFileName(directory));
        if (string.IsNullOrEmpty(projectName))
            projectName = "unknown";
        var branch = SanitizeForPath(GetCurrentBranch(directory));
        if (string.IsNullOrEmpty(branch))
            branch = "no-branch";
        return (projectName, branch);
    }

    /// <summary>获取当前 git 分支名 — 失败返回 null</summary>
    private static string? GetCurrentBranch(string workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var branch = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            if (p.ExitCode != 0) return null;
            return string.IsNullOrEmpty(branch) || branch == "HEAD" ? null : branch;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionIdFactory] git 分支获取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>清理非法字符 — 剔除路径非法字符与点号（TranscriptFileWriter.ValidateId 白名单仅允许字母数字-_）</summary>
    private static string SanitizeForPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (Array.IndexOf(invalid, c) < 0 && c != '.')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
