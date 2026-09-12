
namespace Core.Prompts.Sections;

/// <summary>
/// Git Worktree部分 - 关于Git工作树的说明
/// </summary>
[PromptSection(Name = "git_worktree", Order = 72)]
public static class GitWorktreeSection
{
    public static string? GetContent()
    {
        var isWorktree = PromptConfigSnapshot.Current.IsGitWorktree;
        if (!isWorktree)
        {
            return null;
        }

        return """
# Git Worktree

这是一个git工作树（worktree）——仓库的隔离副本。
从此目录运行所有命令。
不要`cd`到原始仓库根目录。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("git_worktree", GetContent);
}
