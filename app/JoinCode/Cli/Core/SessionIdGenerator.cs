using JoinCode.Abstractions.Entity;

namespace JoinCode.Cli;

/// <summary>
/// 会话 ID 生成器 — 生成 {yyyyMMdd-HHmm}-{项目名}-{分支名} 格式,方便用户查找分辨。
/// 项目名取当前工作目录名,分支名用 git rev-parse 获取(失败回退 no-branch)。
/// </summary>
public static class SessionIdGenerator
{
    /// <summary>
    /// 生成会话 ID — 格式 {yyyyMMdd-HHmm}-{项目名}-{分支名}
    /// </summary>
    /// <param name="workingDirectory">工作目录(默认 Environment.CurrentDirectory)</param>
    /// <param name="createdAt">创建时间(默认 UtcNow)</param>
    /// <returns>可读的会话 ID,如 20260822-1512-myproject-w2</returns>
    public static string Generate(string? workingDirectory = null, DateTime? createdAt = null)
    {
        // T10：委托统一工厂 — 五段式 {日期}-{项目名}-{分支}-parent-{ObjectId全局递增数}
        return global::Core.Utils.SessionIdFactory.CreateParent(workingDirectory, createdAt);
    }

    /// <summary>获取当前 git 分支名 — 失败返回 null</summary>
    private static string? GetCurrentBranch(string workingDir)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var branch = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            if (p.ExitCode != 0) return null;
            return string.IsNullOrEmpty(branch) || branch == "HEAD" ? null : branch;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionIdGenerator] git 分支获取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>清理路径非法字符 — 文件夹名不能含 / \ : * ? " &lt; &gt; |</summary>
    private static string SanitizeForPath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (Array.IndexOf(invalid, c) < 0)
                sb.Append(c);
        }
        return sb.ToString();
    }
}
