namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — AI 工具拦截器（7 个 toggle 开关）+ 快速提示词。
/// 拦截机制：委托到 <see cref="IJccChatSession.UpdateToolBlacklist"/> → IToolHealthMonitor.UpdateBlacklist
/// （双变量原子切换，立即生效）。AI 调用被拦截的工具时由 ToolHealthScoringMiddleware 拒绝。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>拦截器面板是否展开</summary>
    [ObservableProperty]
    private bool _isInterceptorPanelOpen;

    /// <summary>git_commit 拦截开关 — 开启时 AI 调用 git_commit 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockGitCommit;

    /// <summary>git_push 拦截开关 — 开启时 AI 调用 git_push 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockGitPush;

    /// <summary>gh_pr_merge 拦截开关 — 开启时 AI 调用 gh_pr_merge 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockGhPrMerge;

    /// <summary>gh_pr_create 拦截开关 — 开启时 AI 调用 gh_pr_create 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockGhPrCreate;

    /// <summary>gh_release_delete 拦截开关 — 开启时 AI 调用 gh_release_delete 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockGhReleaseDelete;

    /// <summary>file_delete 拦截开关 — 开启时 AI 调用 file_delete 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockFileDelete;

    /// <summary>bash/powershell 拦截开关 — 开启时 AI 调用 bash 和 powershell 工具被拒绝</summary>
    [ObservableProperty]
    private bool _blockShell;

    partial void OnBlockGitCommitChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockGitPushChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockGhPrMergeChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockGhPrCreateChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockGhReleaseDeleteChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockFileDeleteChanged(bool value) => ApplyInterceptorBlacklist();
    partial void OnBlockShellChanged(bool value) => ApplyInterceptorBlacklist();

    /// <summary>
    /// 应用拦截器黑名单到引擎 — 收集所有开启的开关对应工具名，委托到 session。
    /// 每次任意开关变更时调用，全量重建黑名单集合原子替换。
    /// </summary>
    private void ApplyInterceptorBlacklist() {
        var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (BlockGitCommit) blacklist.Add("git_commit");
        if (BlockGitPush) blacklist.Add("git_push");
        if (BlockGhPrMerge) blacklist.Add("gh_pr_merge");
        if (BlockGhPrCreate) blacklist.Add("gh_pr_create");
        if (BlockGhReleaseDelete) blacklist.Add("gh_release_delete");
        if (BlockFileDelete) blacklist.Add("file_delete");
        if (BlockShell) {
            blacklist.Add("bash");
            blacklist.Add("powershell");
        }
        _session.UpdateToolBlacklist(blacklist);
    }

    /// <summary>展开/收拢拦截器面板</summary>
    [RelayCommand]
    private void ToggleInterceptorPanel() => IsInterceptorPanelOpen = !IsInterceptorPanelOpen;

    /// <summary>快速提示词标签列表 — 点击填充到输入框（不直接发送）</summary>
    public IReadOnlyList<string> QuickPrompts { get; } =
    [
        "审核代码",
        "解释代码",
        "重构代码",
        "写单元测试",
        "优化性能",
        "添加注释"
    ];

    /// <summary>快速提示词标签 → 完整提示词文本映射</summary>
    private static string GetQuickPromptText(string label) => label switch {
        "审核代码" => "请审核当前代码变更，指出潜在问题、风格问题、安全风险，并给出改进建议。",
        "解释代码" => "请解释这段代码的作用、设计思路和关键逻辑。",
        "重构代码" => "请重构这段代码，提升可读性和可维护性，保持功能不变。",
        "写单元测试" => "请为这段代码编写单元测试，覆盖主要分支和边界条件。",
        "优化性能" => "请优化这段代码的性能，减少内存分配和 GC 压力。",
        "添加注释" => "请为这段代码添加 XML 文档注释。",
        _ => label
    };

    /// <summary>使用快速提示词 — 填充完整提示词到输入框（不直接发送）</summary>
    [RelayCommand]
    private void UseQuickPrompt(string? prompt) {
        if (!string.IsNullOrWhiteSpace(prompt))
            InputText = GetQuickPromptText(prompt);
    }
}
