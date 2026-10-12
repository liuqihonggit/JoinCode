namespace Infrastructure.Shell;

/// <summary>
/// CI 运行状态检查器 — 查询指定分支的 GitHub Actions CI 是否有 in_progress 的 run。
/// <para>
/// 用于 git push 守卫：push 前检测对应分支 CI 是否在跑，避免 cancel-in-progress 取消正在跑的 run。
/// 实现方负责解析 git remote(owner/repo) 和当前分支(当 branch 为 null 时)。
/// </para>
/// </summary>
public interface ICiRunStatusChecker {
    /// <summary>
    /// 检查指定分支的 CI 是否有 in_progress 的 run。
    /// </summary>
    /// <param name="workingDirectory">git 工作目录路径（用于解析 remote 和当前分支）</param>
    /// <param name="branch">目标分支名；null 表示用当前分支</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>CI 运行状态；非 GitHub 仓库或查询失败返回 IsRunning=false</returns>
    ValueTask<CiRunStatus> CheckBranchCiStatusAsync(string workingDirectory, string? branch, CancellationToken ct);
}

/// <summary>
/// CI 运行状态结果。
/// </summary>
/// <param name="IsRunning">是否有 in_progress 的 CI run</param>
/// <param name="Branch">检查的分支名</param>
/// <param name="RunCount">in_progress 的 run 数量</param>
/// <param name="RunUrl">最新 run 的 URL（可选）</param>
public record CiRunStatus(bool IsRunning, string Branch, int RunCount, string? RunUrl);
