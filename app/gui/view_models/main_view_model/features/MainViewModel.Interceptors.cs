namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — AI 工具拦截器（7 个 toggle 开关）+ 快速提示词。
/// 拦截机制：委托到 <see cref="IJccChatSession.UpdateToolBlacklist"/> → IToolHealthMonitor.UpdateBlacklist
/// （双变量原子切换，立即生效）。AI 调用被拦截的工具时由 ToolHealthScoringMiddleware 拒绝。
/// </summary>
public sealed partial class MainViewModel {
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

    // === 磁盘根保护（ADR 0123）— 扫描盘号列表，默认勾选，用户可取消 ===

    /// <summary>受保护盘号列表 — 启动时扫描磁盘盘号填充，每项默认勾选（保护开启）</summary>
    public ObservableCollection<DriveProtectionItem> ProtectedDrives { get; } = [];

    /// <summary>扫描可用盘号并填充 ProtectedDrives 集合 — 构造时调用，每项默认 IsProtected=true</summary>
    private void InitializeProtectedDrives() {
        foreach (var drive in System.IO.DriveInfo.GetDrives()) {
            if (!drive.IsReady)
                continue;
            var letter = char.ToUpperInvariant(drive.Name[0]).ToString() + ":";
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? letter
                : $"{letter} {drive.VolumeLabel}";
            var item = new DriveProtectionItem(letter, label);
            item.ProtectionChanged += _ => ApplyProtectedDrives();
            ProtectedDrives.Add(item);
        }
        ApplyProtectedDrives();
    }

    /// <summary>应用保护盘号到引擎 — 收集所有勾选的盘号，委托到 session（ADR 0123）</summary>
    private void ApplyProtectedDrives() {
        var protectedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ProtectedDrives) {
            if (item.IsProtected)
                protectedSet.Add(item.DriveLetter);
        }
        _session.UpdateProtectedDrives(protectedSet);
    }

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

    #region Goal 进行中按钮 — 状态显示 + 停止（再三确认由引擎层自动注入提示词 a→b→c）

    /// <summary>goal 是否正在运行 — 驱动 TopBar "🎯 goal 进行中" 按钮显隐</summary>
    [ObservableProperty]
    private bool _isGoalRunning;

    /// <summary>goal 进度文本（如 "2/5 · explorer"）— 驱动 TopBar goal 按钮进度显示</summary>
    [ObservableProperty]
    private string _goalProgressText = string.Empty;

    /// <summary>刷新 goal 进度文本 — 从 IJccChatSession.GetGoalProgressAsync 拉取</summary>
    public async Task RefreshGoalProgressAsync() {
        if (!IsGoalRunning) {
            GoalProgressText = string.Empty;
            return;
        }
        var progress = await _session.GetGoalProgressAsync();
        GoalProgressText = progress is null
            ? string.Empty
            : string.IsNullOrEmpty(progress.CurrentNodeName)
                ? progress.ProgressText
                : $"{progress.ProgressText} · {progress.CurrentNodeName}";
    }

    /// <summary>
    /// 停止 goal — 直接执行 /goal clear。
    /// 再三确认（a→b→c 提示词逐级注入）由引擎层 GoalEngine 自动处理，GUI 不重复。
    /// </summary>
    [RelayCommand]
    private async Task StopGoalAsync() {
        IsGoalRunning = false;
        if (ActiveSidePanel == SidePanelKind.Goal)
            ActiveSidePanel = SidePanelKind.None;
        await _session.ExecuteSlashCommandAsync("/goal clear");
        StatusText = "goal 已停止";
    }

    #endregion

    #region 权限模式切换

    /// <summary>当前权限模式 — GUI 层 6 种状态(Plan/Ask绿/Ask黄/Ask红/Bypass/Unattended)
    /// 引擎层 Auto 映射为 Ask+Green。Shift+Tab 循环 Plan→Ask绿→Ask黄→Ask红→Plan</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionModeDisplay))]
    [NotifyPropertyChangedFor(nameof(PermissionModeLightColor))]
    [NotifyPropertyChangedFor(nameof(PermissionModeToolTip))]
    [NotifyPropertyChangedFor(nameof(PermissionModeIcon))]
    private PermissionMode _currentPermissionMode = PermissionMode.Ask;

    /// <summary>Ask 模式灯色等级 — 绿/黄/红，区分不同危险等级的询问确认</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionModeDisplay))]
    [NotifyPropertyChangedFor(nameof(PermissionModeLightColor))]
    [NotifyPropertyChangedFor(nameof(PermissionModeToolTip))]
    [NotifyPropertyChangedFor(nameof(PermissionModeIcon))]
    private AskLightLevel _askLight = AskLightLevel.Green;

    /// <summary>权限模式显示文本 — 模式图标+纯文本(灯色由 Ellipse 圆点展示)</summary>
    public string PermissionModeDisplay => CurrentPermissionMode switch {
        PermissionMode.Plan => "📋 Plan",
        PermissionMode.Ask => AskLight switch {
            AskLightLevel.Green => "Ask",
            AskLightLevel.Yellow => "Ask",
            AskLightLevel.Red => "Ask",
            _ => "Ask"
        },
        PermissionMode.Auto => "Ask",
        PermissionMode.Bypass => "🔓 Bypass",
        PermissionMode.Unattended => "🤖 Unattended",
        _ => "Ask"
    };

    /// <summary>权限模式灯色 — UI 灯色指示器 Ellipse 填充色</summary>
    public string PermissionModeLightColor => CurrentPermissionMode switch {
        PermissionMode.Plan => "#80b3ff",
        PermissionMode.Ask => AskLight switch {
            AskLightLevel.Green => "#3dd68c",
            AskLightLevel.Yellow => "#ffc107",
            AskLightLevel.Red => "#e5484d",
            _ => "#3dd68c"
        },
        PermissionMode.Auto => "#3dd68c",
        PermissionMode.Bypass => "#e5484d",
        PermissionMode.Unattended => "#8b7ee0",
        _ => "#3dd68c"
    };

    /// <summary>权限模式提示文本</summary>
    public string PermissionModeToolTip => CurrentPermissionMode switch {
        PermissionMode.Plan => "📋 Plan：AI 只规划不执行（蓝灯）",
        PermissionMode.Ask => AskLight switch {
            AskLightLevel.Green => "🟢 Ask 绿灯：可撤回操作需确认",
            AskLightLevel.Yellow => "🟡 Ask 黄灯：未知命令需确认",
            AskLightLevel.Red => "🔴 Ask 红灯：不可撤回操作需确认",
            _ => "Ask 模式"
        },
        PermissionMode.Auto => "🟢 Ask 绿灯：可撤回操作需确认",
        PermissionMode.Bypass => "🔓 Bypass：跳过所有权限检查",
        PermissionMode.Unattended => "🤖 无人值守：红灯自动执行+审计",
        _ => "Ask 模式"
    };

    /// <summary>权限模式图标 — 不同模式显示不同 emoji，工具栏纯图标用</summary>
    public string PermissionModeIcon => CurrentPermissionMode switch {
        PermissionMode.Plan => "📋",
        PermissionMode.Ask => AskLight switch {
            AskLightLevel.Green => "🟢",
            AskLightLevel.Yellow => "🟡",
            AskLightLevel.Red => "🔴",
            _ => "🟢"
        },
        PermissionMode.Bypass => "🔓",
        PermissionMode.Unattended => "🤖",
        _ => "🟢"
    };

    /// <summary>循环切换权限模式 — Shift+Tab 触发(Plan→Ask绿→Ask黄→Ask红→Plan)
    /// Bypass/Unattended 不参与循环（安全设计：Bypass 仅 CLI --bypass，Unattended 仅设置面板开关）</summary>
    [RelayCommand]
    private async Task CyclePermissionModeAsync() {
        // 6 种 GUI 状态循环：Plan → Ask绿 → Ask黄 → Ask红 → Plan
        if (CurrentPermissionMode == PermissionMode.Plan) {
            CurrentPermissionMode = PermissionMode.Ask;
            AskLight = AskLightLevel.Green;
        } else if (CurrentPermissionMode == PermissionMode.Ask || CurrentPermissionMode == PermissionMode.Auto) {
            if (AskLight == AskLightLevel.Green) {
                AskLight = AskLightLevel.Yellow;
            } else if (AskLight == AskLightLevel.Yellow) {
                AskLight = AskLightLevel.Red;
            } else {
                CurrentPermissionMode = PermissionMode.Plan;
                AskLight = AskLightLevel.Green;
            }
        }
        // Bypass/Unattended 保持不变
        await _session.SetPermissionModeAsync(CurrentPermissionMode == PermissionMode.Auto ? PermissionMode.Ask : CurrentPermissionMode);
        StatusText = $"权限模式: {PermissionModeDisplay}";
    }

    /// <summary>设置特定权限模式 — 鼠标点击 MenuFlyout 选项触发（区别于 Shift+Tab 循环）
    /// 参数: "Plan" / "AskGreen" / "AskYellow" / "AskRed" / "Bypass" / "Unattended"</summary>
    [RelayCommand]
    private async Task SetPermissionModeAsync(string mode) {
        switch (mode) {
            case "Plan":
                CurrentPermissionMode = PermissionMode.Plan;
                break;
            case "AskGreen":
                CurrentPermissionMode = PermissionMode.Ask;
                AskLight = AskLightLevel.Green;
                break;
            case "AskYellow":
                CurrentPermissionMode = PermissionMode.Ask;
                AskLight = AskLightLevel.Yellow;
                break;
            case "AskRed":
                CurrentPermissionMode = PermissionMode.Ask;
                AskLight = AskLightLevel.Red;
                break;
            case "Bypass":
                CurrentPermissionMode = PermissionMode.Bypass;
                break;
            case "Unattended":
                CurrentPermissionMode = PermissionMode.Unattended;
                break;
        }
        await _session.SetPermissionModeAsync(CurrentPermissionMode == PermissionMode.Auto ? PermissionMode.Ask : CurrentPermissionMode);
        StatusText = $"权限模式: {PermissionModeDisplay}";
        AddStatusLog($"权限模式 → {PermissionModeDisplay}");
    }

    #endregion
}
