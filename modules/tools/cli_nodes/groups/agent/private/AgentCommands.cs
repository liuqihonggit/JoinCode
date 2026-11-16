using System.Collections.Frozen;
using System.Diagnostics;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Agent;

/// <summary>
/// agent 组的本地实现 —— 只放「不读宿主状态」的工具。
/// <para>worktree 的 create/remove/status/cleanup 依赖宿主的 agent→worktree 会话映射，
/// 在 exe 内另建一份会让宿主的 status 看不到本 exe 建的 worktree（两份真相源），故仍然转发。
/// find_git / list_all 是纯 git 查询，没有任何宿主状态，可零依赖等价实现。</para>
/// </summary>
internal static partial class AgentCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["worktree_find_git"] = FindGitAsync,
            ["worktree_list_all"] = ListAllAsync,
            // 状态文件化工具（~/.jcc/tasks、todo、plans 等，单一真相源）
            ["task_create"] = TaskCreateAsync,
            ["task_list"] = TaskListAsync,
            ["task_get"] = TaskGetAsync,
            ["task_update"] = TaskUpdateAsync,
            ["task_output"] = TaskOutputAsync,
            ["task_set_dependency"] = TaskSetDependencyAsync,
            ["task_remove_dependency"] = TaskRemoveDependencyAsync,
            ["task_get_dependencies"] = TaskGetDependenciesAsync,
            ["task_can_execute"] = TaskCanExecuteAsync,
            ["todo_write"] = TodoWriteAsync,
            ["todo_list"] = TodoListAsync,
            ["todo_update"] = TodoUpdateAsync,
            ["get_plan_status"] = GetPlanStatusAsync,
            ["add_plan_step"] = AddPlanStepAsync,
            ["approve_plan_step"] = ApprovePlanStepAsync,
            ["reject_plan_step"] = RejectPlanStepAsync,
            ["modify_plan_step"] = ModifyPlanStepAsync,
            ["remove_plan_step"] = RemovePlanStepAsync,
            ["execute_plan_steps"] = ExecutePlanStepsAsync,
            ["cron_validate"] = CronValidateAsync,
            ["complete_step"] = CompleteStepAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static async Task<int> FindGitAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        var start = Get(options, "start_path", Directory.GetCurrentDirectory());
        if (!Directory.Exists(start)) {
            Console.Error.WriteLine($"起始路径不存在: {start}");
            return 2;
        }
        var root = await ResolveRootAsync(start, ct).ConfigureAwait(false);
        if (root is null) {
            Console.Error.WriteLine($"在 {start} 及其上级目录都没有找到 Git 仓库（.git）。");
            Console.Error.WriteLine("修复方式: 传入 --start_path 指向仓库内的目录，或先 git init。");
            return 2;
        }
        Console.Out.WriteLine("== 找到 Git 仓库根 ==");
        Console.Out.WriteLine($"起始路径   : {start}");
        Console.Out.WriteLine($"仓库根路径 : {root}");
        var worktrees = await ListWorktreesAsync(root, ct).ConfigureAwait(false);
        Console.Out.WriteLine();
        Console.Out.WriteLine($"已有 Worktree ({worktrees.Count} 个):");
        foreach (var item in worktrees) Console.Out.WriteLine($"  - {item.Path}");
        return 0;
    }

    private static async Task<int> ListAllAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        var root = Get(options, "git_root", string.Empty);
        if (root.Length == 0) {
            root = await ResolveRootAsync(Directory.GetCurrentDirectory(), ct).ConfigureAwait(false) ?? string.Empty;
        } else {
            root = await ResolveRootAsync(root, ct).ConfigureAwait(false) ?? root;
        }
        if (root.Length == 0) {
            Console.Error.WriteLine("当前目录不在 Git 仓库内，且未提供 --git_root。");
            Console.Error.WriteLine("修复方式: jcc-tool-agent.exe worktree_list_all --git_root <仓库根>");
            return 2;
        }
        var worktrees = await ListWorktreesAsync(root, ct).ConfigureAwait(false);
        Console.Out.WriteLine("== Worktree 列表 ==");
        Console.Out.WriteLine($"仓库根: {root}");
        Console.Out.WriteLine($"共 {worktrees.Count} 个");
        Console.Out.WriteLine();
        foreach (var item in worktrees) {
            // 主工作区与附加工作区用不同前缀区分：前者是仓库根自身，后者挂在 .git/worktrees 下。
            var marker = item.IsMain ? "*" : " ";
            var revision = item.Revision.Length > 12 ? item.Revision[..12] : item.Revision;
            Console.Out.WriteLine($"{marker} {item.Path}  [{item.Branch}, {revision}]");
        }
        return 0;
    }

    private readonly record struct Worktree(string Path, string Revision, string Branch, bool IsMain);

    /// <summary>用 <c>git worktree list --porcelain</c> 取结构化结果，避免解析默认表格输出。</summary>
    private static async Task<List<Worktree>> ListWorktreesAsync(string root, CancellationToken ct) {
        var (code, stdout, _) = await RunGitAsync(root, ["worktree", "list", "--porcelain"], ct).ConfigureAwait(false);
        if (code != 0) return [];
        var result = new List<Worktree>();
        var path = string.Empty;
        var revision = string.Empty;
        var branch = string.Empty;
        var isMain = true;
        foreach (var line in stdout.Split('\n')) {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) {
                if (path.Length > 0) result.Add(new Worktree(path, revision, branch, isMain));
                path = string.Empty;
                revision = string.Empty;
                branch = string.Empty;
                isMain = false;
                continue;
            }
            if (trimmed.StartsWith("worktree ", StringComparison.Ordinal)) path = trimmed[9..];
            else if (trimmed.StartsWith("HEAD ", StringComparison.Ordinal)) revision = trimmed[5..];
            else if (trimmed.StartsWith("branch ", StringComparison.Ordinal)) branch = ShortenBranch(trimmed[7..]);
            else if (trimmed.StartsWith("detached", StringComparison.Ordinal)) branch = "detached";
        }
        if (path.Length > 0) result.Add(new Worktree(path, revision, branch, isMain));
        return result;
    }

    private static string ShortenBranch(string reference) {
        const string heads = "refs/heads/";
        return reference.StartsWith(heads, StringComparison.Ordinal) ? reference[heads.Length..] : reference;
    }

    /// <summary>向上找仓库根 —— 交给 git 自己判断，避免手写 .git 目录探测漏掉 worktree / submodule。</summary>
    private static async Task<string?> ResolveRootAsync(string startPath, CancellationToken ct) {
        var (code, stdout, _) = await RunGitAsync(startPath, ["rev-parse", "--show-toplevel"], ct).ConfigureAwait(false);
        if (code != 0) return null;
        var root = stdout.Trim().Replace('\\', '/');
        return root.Length == 0 ? null : root;
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunGitAsync(
        string workingDirectory, string[] arguments, CancellationToken ct) {
        var psi = new ProcessStartInfo("git") {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        try {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 git 进程，请确认 git 已安装并在 PATH 中。");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        } catch (System.ComponentModel.Win32Exception ex) {
            throw new InvalidOperationException($"无法启动 git 进程（PATH 中找不到 git）: {ex.Message}", ex);
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
