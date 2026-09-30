
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// VCS 目录排除集合 — 统一 RgEngine/SearchService 两处重复定义
/// <para>搜索时排除这些版本控制目录及其内容</para>
/// </summary>
public static class VcsDirectoryExclusions {
    private static readonly FrozenSet<string> Directories = FrozenSet.ToFrozenSet(
        [".git", ".svn", ".hg", ".bzr", ".jj", ".sl"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// VCS 目录名集合（.git/.svn/.hg/.bzr/.jj/.sl）
    /// </summary>
    public static FrozenSet<string> Names => Directories;

    /// <summary>
    /// VCS 目录 glob 模式集合 — 每个目录生成两个模式: 目录名 + 目录名/**
    /// 用于 SecurityPatterns.SensitiveFilePattern.VcsInternal(委托消费)
    /// 含 12 个模式: 6 目录 + 6 glob(.git/.git/**/.svn/.svn/**/.hg/.hg/**/.bzr/.bzr/**/.jj/.jj/**/.sl/.sl/**)
    /// </summary>
    public static FrozenSet<string> GlobPatterns { get; } = BuildGlobPatterns();

    /// <summary>
    /// 判断相对路径是否包含 VCS 目录段
    /// </summary>
    public static bool IsVcsPath(string rel) {
        foreach (var vcs in Directories)
            if (rel.Contains($"/{vcs}/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith($"{vcs}/", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static FrozenSet<string> BuildGlobPatterns() {
        var patterns = new string[Directories.Count * 2];
        var i = 0;
        foreach (var d in Directories) {
            patterns[i++] = d;
            patterns[i++] = d + "/**";
        }
        return patterns.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}