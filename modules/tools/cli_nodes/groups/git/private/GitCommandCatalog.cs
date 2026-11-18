

namespace JoinCode.CliNode.Git;

/// <summary>
/// 分组内工具 → git 命令的映射。参数名与 [McpToolParameter] 特性保持一致（见 help.txt）。
/// 描述数据来自现有特性，本文件只负责「怎么执行」，不重复写任何说明。
/// </summary>
internal static class GitCommandCatalog {
    private static readonly FrozenDictionary<string, Func<ArgBag, string>> Builders =
        new Dictionary<string, Func<ArgBag, string>>(StringComparer.Ordinal) {
            ["git_status"] = StatusArgs,
            ["git_add"] = AddArgs,
            ["git_commit"] = CommitArgs,
            ["git_push"] = PushArgs,
            ["git_pull"] = PullArgs,
            ["git_log"] = LogArgs,
            ["git_diff"] = DiffArgs,
            ["git_branch"] = BranchArgs,
            ["git_clone"] = CloneArgs,
            ["git_submodule_update"] = SubmoduleUpdateArgs
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IEnumerable<string> ToolIds => Builders.Keys.OrderBy(id => id, StringComparer.Ordinal);

    public static bool TryGet(string toolId, out Func<ArgBag, string> builder) =>
        Builders.TryGetValue(toolId, out builder!);

    private static string StatusArgs(ArgBag a) => "status --porcelain -b";

    private static string AddArgs(ArgBag a) =>
        $"add {Quote(OptionalFirst(a, "path", "."))}";

    private static string CommitArgs(ArgBag a) {
        var message = RequiredFirst(a, "message",
            "git_commit 缺少必填参数 --message（由 [McpToolParameter] 标记为必填）。正确写法: jcc-tool-git.exe git_commit --message \"...\"");
        var allowEmpty = IsTrue(a.Get("allow_empty"));
        return $"commit -m {Quote(message)}{(allowEmpty ? " --allow-empty" : "")}";
    }

    private static string PushArgs(ArgBag a) {
        var force = IsTrue(a.Get("force")) ? " --force" : "";
        return JoinTokens("push" + force, a.Get("remote", "origin"), a.Get("branch"));
    }

    private static string PullArgs(ArgBag a) =>
        JoinTokens("pull", a.Get("remote", "origin"), a.Get("branch"));

    private static string LogArgs(ArgBag a) {
        var count = a.Get("count", "10");
        var format = a.Get("format", "oneline") switch {
            "short" => "--pretty=short",
            "full" => "--pretty=fuller",
            _ => "--oneline"
        };
        return $"log {format} -n {count}";
    }

    private static string DiffArgs(ArgBag a) {
        var mode = a.Get("mode", "worktree");
        var staged = mode is "staged" or "cached" ? " --cached" : "";
        var path = a.Get("path");
        var suffix = string.IsNullOrWhiteSpace(path) ? "" : $" -- {Quote(path)}";
        return $"diff{staged}{suffix}";
    }

    private static string BranchArgs(ArgBag a) {
        var name = RequiredFirst(a, "branch_name",
            "git_branch 缺少必填参数 --branch_name。正确写法: jcc-tool-git.exe git_branch --branch_name feature/x");
        var operation = a.Get("operation", "switch");
        var bas = a.Get("base_branch");
        return operation switch {
            "create" => JoinTokens($"checkout -b {Quote(name)}", bas is null ? null : Quote(bas)),
            "delete" => $"branch -D {Quote(name)}",
            _ => $"checkout {Quote(name)}"
        };
    }

    private static string CloneArgs(ArgBag a) {
        var url = RequiredFirst(a, "url",
            "git_clone 缺少必填参数 --url。正确写法: jcc-tool-git.exe git_clone --url https://...");
        var branch = a.Get("branch");
        var branchArg = string.IsNullOrWhiteSpace(branch) ? "" : $"--branch {Quote(branch)} ";
        var recursive = IsTrue(a.Get("recursive")) ? "--recursive " : "";
        var directory = a.Get("directory");
        var target = string.IsNullOrWhiteSpace(directory) ? "" : $" {Quote(directory)}";
        return $"clone {branchArg}{recursive}{Quote(url)}{target}";
    }

    private static string SubmoduleUpdateArgs(ArgBag a) {
        var jobs = a.Get("jobs") ?? (Environment.ProcessorCount * 2).ToString();
        var depth = a.Get("depth", "1");
        return $"submodule update --init --recursive --jobs {jobs} --depth {depth}";
    }

    private static string OptionalFirst(ArgBag a, string key, string fallback) =>
        a.Get(key) ?? (a.Positional.Length > 0 ? a.Positional[0] : fallback);

    private static string RequiredFirst(ArgBag a, string key, string errorMessage) =>
        a.Get(key) ?? (a.Positional.Length > 0 ? a.Positional[0] : throw new ArgumentException(errorMessage));

    private static bool IsTrue(string? raw) =>
        raw is not null && raw.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string JoinTokens(params string?[] tokens) =>
        string.Join(" ", tokens.Where(token => !string.IsNullOrWhiteSpace(token)));

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
