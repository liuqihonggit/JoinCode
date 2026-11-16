// agent 组 worktree_* 下沉 —— 走 git 原生命令，git 自身就是真相源（`git worktree list`），
// 不再依赖宿主的进程内 session 注册表。路径/分支命名沿用宿主约定：
//   {gitRoot}/.jcc/worktrees/{agentId 的 / \ 换成 +}，分支 worktree-{同上}
using System.Globalization;
using System.Text;

namespace JoinCode.CliNode.Agent;

internal static partial class AgentCommands {
    private static string WorktreesDir(string gitRoot) => Path.Combine(gitRoot, ".jcc", "worktrees");

    /// <summary>与 AgentWorktreeSession.GenerateBranchName/GenerateWorktreePath 同一套 safeId 规则。</summary>
    private static string SafeAgentId(string agentId) => agentId.Replace("/", "+").Replace("\\", "+");

    private static string BranchFor(string agentId) => "worktree-" + SafeAgentId(agentId);

    private static string WorktreePathFor(string gitRoot, string agentId) => Path.Combine(WorktreesDir(gitRoot), SafeAgentId(agentId));

    private static string? Opt(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static async Task<string> RequireGitRootAsync(IReadOnlyDictionary<string, string> o, string tool, CancellationToken ct) {
        var start = Opt(o, "git_root") ?? Directory.GetCurrentDirectory();
        var root = await ResolveMainRootAsync(start, ct).ConfigureAwait(false);
        return root ?? throw new ArgumentException($"{tool}: 在 '{start}' 及其上级都找不到 git 仓库。传入 --git_root 指向仓库根，或先在仓库内执行。");
    }

    /// <summary>
    /// 解析「主仓库根」而不是「当前工作树根」。
    /// <para>在 worktree 里执行时 <c>rev-parse --show-toplevel</c> 返回的是 worktree 自己，
    /// 再拼 .jcc/worktrees/{id} 就会套娃。改用 <c>--git-common-dir</c>（worktree 共享同一 .git）锁定主仓库。</para>
    /// </summary>
    private static async Task<string?> ResolveMainRootAsync(string startPath, CancellationToken ct) {
        var (code, stdout, _) = await RunGitAsync(startPath, ["rev-parse", "--git-common-dir"], ct).ConfigureAwait(false);
        if (code != 0 || stdout.Trim().Length == 0) return null;
        var commonDir = stdout.Trim();
        var absolute = Path.GetFullPath(Path.IsPathRooted(commonDir) ? commonDir : Path.Combine(startPath, commonDir));
        // 常规仓库: <root>/.git → 根是上一级；bare 仓库本身就是根
        return string.Equals(Path.GetFileName(absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), ".git", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : absolute;
    }

    // ==================== worktree_create ====================

    private static async Task<int> WorktreeCreateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var agentId = Required(o, "agent_id", "worktree_create");
        var gitRoot = await RequireGitRootAsync(o, "worktree_create", ct).ConfigureAwait(false);
        var path = WorktreePathFor(gitRoot, agentId);
        var branch = BranchFor(agentId);
        var baseRef = Opt(o, "base_branch") ?? "HEAD";

        if (Directory.Exists(path)) {
            Console.Error.WriteLine($"worktree 目录已存在: {path}");
            Console.Error.WriteLine($"要重建请先 worktree_remove --agent_id {agentId} --force");
            return 1;
        }
        Directory.CreateDirectory(WorktreesDir(gitRoot));

        // 分支已存在就复用，否则从 baseRef 新建 —— 重复 create 不会因为"分支已存在"失败
        var (branchExists, _, _) = await RunGitAsync(gitRoot, ["rev-parse", "--verify", "refs/heads/" + branch], ct).ConfigureAwait(false);
        string[] arguments = branchExists == 0
            ? ["worktree", "add", path, branch]
            : ["worktree", "add", "-b", branch, path, baseRef];

        var (code, stdout, stderr) = await RunGitAsync(gitRoot, arguments, ct).ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine($"git worktree add 失败（exit {code.ToString(CultureInfo.InvariantCulture)}）");
            Console.Error.WriteLine(stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim());
            Console.Error.WriteLine("常见原因: 目标路径被占用 / 该分支已被别的 worktree 检出。先 worktree_list 看看。");
            return 1;
        }

        var (headCode, headOut, _) = await RunGitAsync(path, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);
        Console.Out.WriteLine($"worktree 已创建: {agentId}");
        Console.Out.WriteLine($"  路径: {path}");
        Console.Out.WriteLine($"  分支: {branch}{(branchExists == 0 ? "（复用已有分支）" : $"（基于 {baseRef} 新建）")}");
        if (headCode == 0) Console.Out.WriteLine($"  HEAD: {headOut.Trim()}");
        return 0;
    }

    // ==================== worktree_list ====================

    private static async Task<int> WorktreeListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var gitRoot = await RequireGitRootAsync(o, "worktree_list", ct).ConfigureAwait(false);
        var worktrees = await ListWorktreesAsync(gitRoot, ct).ConfigureAwait(false);
        var managed = worktrees.Where(w => IsManagedPath(gitRoot, w.Path)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"git 工作树 {worktrees.Count} 个（jcc 管理 {managed.Count} 个）");
        sb.AppendLine($"仓库根: {gitRoot}");
        sb.AppendLine();
        foreach (var worktree in worktrees) {
            var marker = IsManagedPath(gitRoot, worktree.Path) ? "[jcc]" : "[主仓/其他]";
            sb.AppendLine($"  {marker} {worktree.Path}");
            sb.AppendLine($"      分支: {(worktree.Branch.Length > 0 ? worktree.Branch : "(detached)")}  HEAD: {Short(worktree.Revision)}");
        }
        if (managed.Count == 0) {
            sb.AppendLine("  （还没有 jcc 创建的 worktree，用 worktree_create --agent_id <id> 创建）");
        }
        Console.Out.Write(sb.ToString());
        return 0;
    }

    private static bool IsManagedPath(string gitRoot, string path) {
        var managed = NormalizePath(WorktreesDir(gitRoot));
        return NormalizePath(path).StartsWith(managed, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    private static string Short(string revision) => revision.Length > 12 ? revision[..12] : revision;

    // ==================== worktree_status ====================

    private static async Task<int> WorktreeStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var agentId = Required(o, "agent_id", "worktree_status");
        var gitRoot = await RequireGitRootAsync(o, "worktree_status", ct).ConfigureAwait(false);
        var path = WorktreePathFor(gitRoot, agentId);
        if (!Directory.Exists(path)) {
            Console.Error.WriteLine($"没有 agent '{agentId}' 的 worktree: {path}");
            Console.Error.WriteLine("用 worktree_list 看现有 worktree。");
            return 1;
        }

        var (branchCode, branchOut, _) = await RunGitAsync(path, ["branch", "--show-current"], ct).ConfigureAwait(false);
        var (headCode, headOut, _) = await RunGitAsync(path, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);
        var (statusCode, statusOut, _) = await RunGitAsync(path, ["status", "--porcelain"], ct).ConfigureAwait(false);
        var changed = statusCode == 0
            ? statusOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : -1;
        var (countCode, countOut, _) = await RunGitAsync(path, ["rev-list", "--count", "HEAD", "--not", "--remotes"], ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"worktree 状态: {agentId}");
        sb.AppendLine($"  路径: {path}");
        sb.AppendLine($"  分支: {(branchCode == 0 && branchOut.Trim().Length > 0 ? branchOut.Trim() : "(detached)")}");
        if (headCode == 0) sb.AppendLine($"  HEAD: {headOut.Trim()}");
        sb.AppendLine($"  未提交变更: {(changed < 0 ? "未知" : changed.ToString(CultureInfo.InvariantCulture) + " 个文件")}");
        if (countCode == 0) sb.AppendLine($"  未推送提交: {countOut.Trim()} 个");
        if (changed > 0) {
            sb.AppendLine();
            sb.AppendLine("变更文件:");
            foreach (var line in statusOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(20)) {
                sb.AppendLine("  " + line.TrimEnd());
            }
        }
        Console.Out.Write(sb.ToString());
        return 0;
    }

    // ==================== worktree_remove ====================

    private static async Task<int> WorktreeRemoveAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var agentId = Required(o, "agent_id", "worktree_remove");
        var force = Get(o, "force", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        var gitRoot = await RequireGitRootAsync(o, "worktree_remove", ct).ConfigureAwait(false);
        var path = WorktreePathFor(gitRoot, agentId);
        if (!Directory.Exists(path)) {
            Console.Error.WriteLine($"没有 agent '{agentId}' 的 worktree: {path}");
            return 1;
        }

        if (!force) {
            var (_, statusOut, _) = await RunGitAsync(path, ["status", "--porcelain"], ct).ConfigureAwait(false);
            if (statusOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 0) {
                Console.Error.WriteLine($"worktree 有未提交变更，拒绝移除: {path}");
                Console.Error.WriteLine("先提交/丢弃这些改动，或加 --force true 强制移除（改动会丢失）。");
                return 1;
            }
        }

        string[] arguments = force ? ["worktree", "remove", "--force", path] : ["worktree", "remove", path];
        var (code, stdout, stderr) = await RunGitAsync(gitRoot, arguments, ct).ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine($"git worktree remove 失败（exit {code.ToString(CultureInfo.InvariantCulture)}）");
            Console.Error.WriteLine(stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim());
            return 1;
        }

        var branch = BranchFor(agentId);
        var (branchDeleteCode, _, branchDeleteError) = await RunGitAsync(gitRoot, ["branch", "-D", branch], ct).ConfigureAwait(false);

        Console.Out.WriteLine($"worktree 已移除: {agentId}");
        Console.Out.WriteLine($"  路径: {path}");
        if (branchDeleteCode == 0) Console.Out.WriteLine($"  分支已删除: {branch}");
        else Console.Out.WriteLine($"  分支未删除: {branch}（{branchDeleteError.Trim()}）");
        return 0;
    }

    // ==================== worktree_cleanup ====================

    private static async Task<int> WorktreeCleanupAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var confirm = Get(o, "confirm", "");
        var staleHours = GetInt(o, "stale_hours", 24);
        if (staleHours <= 0) throw new ArgumentException($"stale_hours 必须为正数，当前 {staleHours.ToString(CultureInfo.InvariantCulture)}");

        var gitRoot = await RequireGitRootAsync(o, "worktree_cleanup", ct).ConfigureAwait(false);
        var directory = WorktreesDir(gitRoot);
        if (!Directory.Exists(directory)) {
            Console.Out.WriteLine("没有 jcc worktree 目录，无需清理");
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddHours(-staleHours);
        var candidates = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Directory.GetLastWriteTimeUtc(path) < cutoff)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"worktree 清理（超过 {staleHours.ToString(CultureInfo.InvariantCulture)} 小时未活动）");
        sb.AppendLine($"  目录: {directory}");
        sb.AppendLine($"  候选: {candidates.Count} 个");
        if (candidates.Count == 0) {
            Console.Out.Write(sb.ToString());
            return 0;
        }

        if (!string.Equals(confirm, "yes", StringComparison.OrdinalIgnoreCase)) {
            foreach (var path in candidates) sb.AppendLine($"  将删除: {path}");
            sb.AppendLine();
            sb.AppendLine("这是演练（dry-run）：加 --confirm yes 才会真正删除。有未提交变更的会被跳过。");
            Console.Out.Write(sb.ToString());
            return 0;
        }

        var removed = 0;
        var skipped = 0;
        foreach (var path in candidates) {
            var (_, statusOut, _) = await RunGitAsync(path, ["status", "--porcelain"], ct).ConfigureAwait(false);
            if (statusOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 0) {
                sb.AppendLine($"  跳过（有未提交变更）: {path}");
                skipped++;
                continue;
            }
            var (code, _, stderr) = await RunGitAsync(gitRoot, ["worktree", "remove", "--force", path], ct).ConfigureAwait(false);
            if (code == 0) {
                sb.AppendLine($"  已删除: {path}");
                removed++;
            } else {
                sb.AppendLine($"  删除失败: {path}（{stderr.Trim()}）");
                skipped++;
            }
        }
        await RunGitAsync(gitRoot, ["worktree", "prune"], ct).ConfigureAwait(false);
        sb.AppendLine();
        sb.AppendLine($"清理完成: 删除 {removed.ToString(CultureInfo.InvariantCulture)} 个，跳过 {skipped.ToString(CultureInfo.InvariantCulture)} 个");
        Console.Out.Write(sb.ToString());
        return 0;
    }

    // ==================== worktree_merge ====================

    private static async Task<int> WorktreeMergeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var source = Path.GetFullPath(Required(o, "source_worktree_path", "worktree_merge"));
        var target = Path.GetFullPath(Required(o, "target_worktree_path", "worktree_merge"));
        var strategy = (Opt(o, "strategy") ?? "fail").Trim().ToLowerInvariant();
        if (strategy is not ("fail" or "ours" or "theirs" or "auto_merge"))
            throw new ArgumentException($"strategy 必须是 fail/ours/theirs/auto_merge 之一，当前 '{strategy}'。");

        if (!Directory.Exists(source)) throw new ArgumentException($"source worktree 不存在: {source}");
        if (!Directory.Exists(target)) throw new ArgumentException($"target worktree 不存在: {target}");

        var (sourceBranchCode, sourceBranchOut, _) = await RunGitAsync(source, ["branch", "--show-current"], ct).ConfigureAwait(false);
        var sourceBranch = sourceBranchOut.Trim();
        if (sourceBranchCode != 0 || sourceBranch.Length == 0) {
            throw new ArgumentException($"source worktree 处于 detached HEAD（{source}），没有可合并的分支名。先在 source 里 git switch -c 建分支。");
        }

        var (_, sourceDirty, _) = await RunGitAsync(source, ["status", "--porcelain"], ct).ConfigureAwait(false);
        var dirtyCount = sourceDirty.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        var arguments = new List<string> { "merge", "--no-edit" };
        if (strategy is "ours") arguments.Add("-X");
        if (strategy is "ours") arguments.Add("ours");
        if (strategy is "theirs" or "auto_merge") { arguments.Add("-X"); arguments.Add("theirs"); }
        arguments.Add(sourceBranch);

        var (code, stdout, stderr) = await RunGitAsync(target, [.. arguments], ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"合并 {sourceBranch} → {target}");
        sb.AppendLine($"  策略: {strategy}");
        if (dirtyCount > 0) sb.AppendLine($"  注意: source 有 {dirtyCount.ToString(CultureInfo.InvariantCulture)} 个未提交文件，本次只合并已提交内容");

        if (code != 0) {
            var (_, conflicts, _) = await RunGitAsync(target, ["diff", "--name-only", "--diff-filter=U"], ct).ConfigureAwait(false);
            sb.AppendLine("  合并失败：存在冲突");
            foreach (var file in conflicts.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(20)) {
                sb.AppendLine("    冲突: " + file.TrimEnd());
            }
            await RunGitAsync(target, ["merge", "--abort"], ct).ConfigureAwait(false);
            sb.AppendLine("  已执行 git merge --abort，target 回到合并前状态。");
            sb.AppendLine("  下一步：在 target 里手工解决冲突后重试，或改用 --strategy ours/theirs。");
            Console.Out.Write(sb.ToString());
            return 1;
        }

        sb.AppendLine("  合并成功");
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10)) {
            sb.AppendLine("  " + line.TrimEnd());
        }
        if (stderr.Trim().Length > 0) sb.AppendLine("  git: " + stderr.Trim());
        Console.Out.Write(sb.ToString());
        return 0;
    }
}
