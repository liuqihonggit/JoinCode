namespace IO.ProcessService;

/// <summary>
/// PR Body 自动生成器 — 根据 commit messages、分支名、diff 自动生成 PR 描述
/// <para>
/// 核心价值：
/// 1. 避免用户忘记填写 body
/// 2. 统一 PR 格式
/// 3. 自动提取变更摘要
/// </para>
/// </summary>
public sealed class PrBodyGenerator
{
    private readonly IGitCommandRunner _gitRunner;

    public PrBodyGenerator(IGitCommandRunner gitRunner)
    {
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
    }

    /// <summary>
    /// 从 commit messages 生成 PR body
    /// </summary>
    public async Task<string> GenerateFromCommitsAsync(
        string baseBranch,
        string headBranch,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var result = await _gitRunner.ExecuteAsync(
            $"log {baseBranch}..{headBranch} --pretty=format:%s",
            workingDirectory,
            ct).ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return GenerateFromBranchName(headBranch);
        }

        var commits = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static c => c.Trim())
            .Where(static c => c.Length > 0)
            .ToList();

        if (commits.Count == 0)
        {
            return GenerateFromBranchName(headBranch);
        }

        var sb = new StringBuilder();
        sb.AppendLine("## 变更内容");
        sb.AppendLine();
        foreach (var commit in commits)
        {
            sb.AppendLine($"- {commit}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从分支名生成 PR body
    /// </summary>
    public static string GenerateFromBranchName(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return "## 变更内容\n\n（请在 PR 中描述变更内容）";
        }

        var sb = new StringBuilder();
        sb.AppendLine("## 变更内容");
        sb.AppendLine();
        sb.AppendLine($"分支: `{branchName}`");
        sb.AppendLine();
        sb.AppendLine("### 变更类型");
        sb.AppendLine("- [ ] 新功能");
        sb.AppendLine("- [ ] Bug 修复");
        sb.AppendLine("- [ ] 重构");
        sb.AppendLine("- [ ] 文档更新");
        sb.AppendLine("- [ ] 其他");
        sb.AppendLine();
        sb.AppendLine("### 变更描述");
        sb.AppendLine("（请描述本次变更的内容和目的）");

        return sb.ToString();
    }

    /// <summary>
    /// 从 diff 生成 PR body
    /// </summary>
    public async Task<string> GenerateFromDiffAsync(
        string baseBranch,
        string headBranch,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var result = await _gitRunner.ExecuteAsync(
            $"diff {baseBranch}..{headBranch} --stat",
            workingDirectory,
            ct).ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return GenerateFromBranchName(headBranch);
        }

        var sb = new StringBuilder();
        sb.AppendLine("## 变更文件");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(result.Output.Trim());
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// 使用模板生成 PR body
    /// </summary>
    public static string GenerateWithTemplate(string title, string? description = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine(description);
            sb.AppendLine();
        }
        sb.AppendLine("### 变更类型");
        sb.AppendLine("- [ ] 新功能");
        sb.AppendLine("- [ ] Bug 修复");
        sb.AppendLine("- [ ] 重构");
        sb.AppendLine();
        sb.AppendLine("### 测试");
        sb.AppendLine("（请描述如何测试本次变更）");

        return sb.ToString();
    }
}
