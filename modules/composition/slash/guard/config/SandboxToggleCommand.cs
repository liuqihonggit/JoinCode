
namespace JoinCode.ChatCommands;

/// <summary>
/// /sandbox-toggle 命令 — 切换沙箱模式
/// 支持启用、禁用、查看状态、查看命令排除清单、切换沙箱类型
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.SandboxToggle, Description = "切换沙箱模式", Usage = "/sandbox-toggle [on|off|status|exclude|switch]", Category = ChatCommandCategory.Config, Aliases = ["sandbox"], ArgumentHint = "[on|off|status|exclude|switch]", IsHidden = true)]
[ChatCommandArg("action", Type = "string", Description = "沙箱操作", Enum = new[] { "on", "off", "status", "exclude", "switch" }, Default = "status")]
public sealed class SandboxToggleCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /sandbox-toggle 命令 — 根据子命令分发到对应沙箱操作
    /// 支持 on/off/status/exclude/switch 五种操作,默认 status;
    /// 仅命令词小写化用于分派,路径/类型等参数保留用户输入原大小写传入后续逻辑
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>命令执行结果,始终返回 Continue 表示继续会话</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var sandboxManager = ChatCommandBase.GetService<ISandboxManager>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context);
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCommand = ExtractSubCommand(args);
        var hasArgument = parts.Length > 1;

        if (subCommand is "help" or "--help" or "-h" || (hasArgument && parts[1] is "--help" or "-h")) {
            ShowHelp();
            return ChatCommandResult.Continue();
        }

        var config = ChatCommandBase.GetService<WorkflowConfig>(context);
        if (config?.SandboxPolicy.Locked == true && IsPolicyLockedMutation(subCommand, hasArgument)) {
            WritePolicyLocked(config.SandboxPolicy);
            return ChatCommandResult.Continue();
        }

        if (subCommand is "exclude") {
            HandleExclude(context, args);
        } else if (subCommand is "switch") {
            await HandleSwitchAsync(sandboxManager, context, args).ConfigureAwait(false);
        } else if (subCommand is "on" or "enable") {
            if (sandboxManager is null) {
                ReportUninitialized();
            } else {
                await EnableSandboxAsync(sandboxManager, context).ConfigureAwait(false);
            }
        } else if (subCommand is "off" or "disable") {
            if (sandboxManager is null) {
                ReportUninitialized();
            } else {
                await DisableSandboxAsync(sandboxManager, context).ConfigureAwait(false);
            }
        } else if (subCommand is "status") {
            if (sandboxManager is null) {
                ReportUninitialized();
            } else {
                ShowStatus(sandboxManager, context);
            }
        } else if (subCommand.Length == 0) {
            if (sandboxManager is null) {
                ReportUninitialized();
            } else {
                ShowSettingsMenu(sandboxManager, context);
            }
        } else {
            TerminalHelper.WriteLine($"未知沙箱子命令: {subCommand}");
            ShowHelp();
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>是否为会被企业策略锁定的本地修改 — 只读子命令（status/help/无参数/exclude 无模式）不拦。</summary>
    private static bool IsPolicyLockedMutation(string subCommand, bool hasArgument) => subCommand switch {
        "on" or "enable" or "off" or "disable" => true,
        "switch" => hasArgument,
        "exclude" => hasArgument,
        _ => false,
    };

    /// <summary>输出策略锁原因与下一步 — 对齐「错误提示必须带诱导方式」。</summary>
    private static void WritePolicyLocked(SandboxPolicyConfig policy) {
        var keys = policy.LockedKeys.Count > 0 ? string.Join(", ", policy.LockedKeys) : "sandbox.*";
        TerminalHelper.WriteLine($"沙箱设置被企业策略锁定：managed-settings.json 已定义 {keys}。");
        TerminalHelper.WriteLine("本地 /sandbox 不能修改这些键；请让管理员修改托管策略文件，或移除对应键后重启。");
    }

    /// <summary>管理器缺失时的统一提示（非交互环境保持静默）。</summary>
    private static void ReportUninitialized() {
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxServiceNotInitialized));
        }
    }

    /// <summary>
    /// 提取参数串首个 token 并小写化为分派命令词 —
    /// 后续参数(路径/类型)保留原大小写,输出提示与持久化路径与用户输入保持一致
    /// </summary>
    /// <param name="args">归一化后的命令参数串</param>
    /// <returns>小写化的子命令词;空参数返回空字符串</returns>
    private static string ExtractSubCommand(string args) =>
        (args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty).ToLowerInvariant();

    private static async Task EnableSandboxAsync(ISandboxManager sandboxManager, ChatCommandContext context) {
        var workflow = ChatCommandBase.GetService<WorkflowConfig>(context);
        if (workflow?.SandboxPolicy.Locked == true) {
            WritePolicyLocked(workflow.SandboxPolicy);
            return;
        }

        var confirmed = await Confirmation.ConfirmAsync("确定要启用沙箱模式吗？启用后将限制文件系统和网络访问。", context.CancellationToken).ConfigureAwait(false);
        if (!confirmed) {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxCancelled));
            return;
        }

        try {
            var options = new SandboxOptions {
                Type = SandboxType.Soft,
                RestrictNetwork = true,
                RestrictFileSystem = true,
                AllowedPaths = GetAllowWritePaths(context)
            };
            var info = await sandboxManager.EnterSandboxAsync(options, context.CancellationToken).ConfigureAwait(false);
            await PersistSandboxEnabledAsync(context, enabled: true).ConfigureAwait(false);
            SyncPromptPolicy(context, enabled: true);
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnabled), info.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), info.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxNetworkRestricted), info.IsRestricted ? "是" : "否"));
        } catch (Exception ex) {
            ChatCommandBase.HandleError("启用沙箱模式", ex);
        }
    }

    private static async Task DisableSandboxAsync(ISandboxManager sandboxManager, ChatCommandContext context) {
        var workflow = ChatCommandBase.GetService<WorkflowConfig>(context);
        if (workflow?.SandboxPolicy.Locked == true) {
            WritePolicyLocked(workflow.SandboxPolicy);
            return;
        }

        var confirmed = await Confirmation.ConfirmAsync("确定要禁用沙箱模式吗？", context.CancellationToken).ConfigureAwait(false);
        if (!confirmed) {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxCancelled));
            return;
        }

        try {
            await sandboxManager.ExitSandboxAsync(context.CancellationToken).ConfigureAwait(false);
            await PersistSandboxEnabledAsync(context, enabled: false).ConfigureAwait(false);
            SyncPromptPolicy(context, enabled: false);
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxDisabled));
        } catch (Exception ex) {
            ChatCommandBase.HandleError("禁用沙箱模式", ex);
        }
    }

    /// <summary>
    /// 持久化 <c>sandbox.enabled</c> — /sandbox on/off 从「会话开关」升级为「设置开关」
    /// （对齐基准 /sandbox 写 settings）。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <param name="enabled">目标状态。</param>
    /// <returns>写入成功返回 true；配置服务缺失或异常返回 false（命令仍继续）。</returns>
    internal static async Task<bool> PersistSandboxEnabledAsync(ChatCommandContext context, bool enabled) {
        var workflow = ChatCommandBase.GetService<WorkflowConfig>(context);
        if (workflow?.SandboxPolicy.Locked == true) {
            WritePolicyLocked(workflow.SandboxPolicy);
            return false;
        }

        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null) {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxConfigUnavailable));
            return false;
        }

        try {
            var written = await configService
                .SetAsync(ConfigKeys.SandboxEnabled, enabled ? "true" : "false", context.CancellationToken)
                .ConfigureAwait(false);
            if (!written) {
                TerminalHelper.WriteLine(L.T(StringKey.HostSandboxConfigUnavailable));
            }
            return written;
        } catch (Exception ex) {
            ChatCommandBase.HandleError("写入 sandbox.enabled", ex);
            return false;
        }
    }

    /// <summary>同步提示词快照 — 让下一轮请求立刻使用新的三段式措辞。</summary>
    /// <param name="context">命令执行上下文。</param>
    /// <param name="enabled">是否启用沙箱。</param>
    private static void SyncPromptPolicy(ChatCommandContext context, bool enabled) {
        var config = ChatCommandBase.GetService<WorkflowConfig>(context);
        BashToolPrompt.SetSandboxPolicy(enabled, config?.Sandbox.AllowUnsandboxedCommands ?? true);
    }

    /// <summary>渲染沙箱状态 — 会话沙箱状态 + 设置开关 + 可用类型。</summary>
    /// <param name="sandboxManager">沙箱管理器。</param>
    /// <param name="context">命令执行上下文（读取 settings 快照）。</param>
    private static void ShowStatus(ISandboxManager sandboxManager, ChatCommandContext context) {
        var isInSandbox = sandboxManager.IsInSandbox;
        var current = sandboxManager.CurrentSandbox;
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxStatusHeader), isInSandbox ? "已启用" : "已禁用"));

        if (current is not null) {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxTypeLabel), current.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), current.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnteredAtLabel), current.EnteredAt));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRestrictedLabel), current.IsRestricted ? "是" : "否"));
        }

        var settingsEnabled = ChatCommandBase.GetService<WorkflowConfig>(context)?.Sandbox.Enabled == true;
        TerminalHelper.WriteLine($"sandbox.enabled 设置: {(settingsEnabled ? "true" : "false")}（/sandbox-toggle on 会写入该设置）");

        var workflow = ChatCommandBase.GetService<WorkflowConfig>(context);
        if (workflow?.SandboxPolicy.Locked == true) {
            TerminalHelper.WriteLine($"策略锁: 已锁定（{string.Join(", ", workflow.SandboxPolicy.LockedKeys)}）");
        } else {
            TerminalHelper.WriteLine("策略锁: 无（本地 /sandbox 可修改）");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"可用沙箱类型: {string.Join(", ", sandboxManager.GetAvailableTypes().Select(t => t.ToValue()))}");

        var platformSupport = SandboxPlatformSupport.Evaluate(SandboxGate.CurrentPlatform);
        TerminalHelper.WriteLine(string.Format(
            L.T(StringKey.HostSandboxStatusPlatformSupported),
            platformSupport.Supported ? "是" : "否"));
        TerminalHelper.WriteLine($"  平台说明: {platformSupport.Reason}");

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageEnable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageDisable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageExclude));
        TerminalHelper.WriteLine("  /sandbox-toggle switch <soft|process|docker|bubblewrap>  — 切换沙箱类型");
        TerminalHelper.WriteLine("  /sandbox-toggle help  — 查看全部子命令与示例");
    }

    /// <summary>无参数时的设置面板 — 对齐基准 /sandbox 无参数进入设置视图；本仓先落可读面板，不改已有子命令。</summary>
    private static void ShowSettingsMenu(ISandboxManager sandboxManager, ChatCommandContext context) {
        ShowStatus(sandboxManager, context);

        var config = ChatCommandBase.GetService<WorkflowConfig>(context);
        var sandbox = config?.Sandbox ?? new SandboxConfig();
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("沙箱设置（settings.json current.sandbox）：");
        TerminalHelper.WriteLine($"  enabled={sandbox.Enabled.ToString().ToLowerInvariant()}  failIfUnavailable={sandbox.FailIfUnavailable.ToString().ToLowerInvariant()}");
        TerminalHelper.WriteLine($"  autoAllowBashIfSandboxed={sandbox.AutoAllowBashIfSandboxed.ToString().ToLowerInvariant()}  allowUnsandboxedCommands={sandbox.AllowUnsandboxedCommands.ToString().ToLowerInvariant()}");
        TerminalHelper.WriteLine($"  excludedCommands: {(sandbox.ExcludedCommands.Count == 0 ? "（无）" : string.Join(", ", sandbox.ExcludedCommands))}");
        TerminalHelper.WriteLine($"  network.allowedDomains: {(sandbox.Network.AllowedDomains.Count == 0 ? "（无）" : string.Join(", ", sandbox.Network.AllowedDomains))}");
        TerminalHelper.WriteLine($"  filesystem.allowWrite: {(sandbox.Filesystem.AllowWrite.Count == 0 ? "（无）" : string.Join(", ", sandbox.Filesystem.AllowWrite))}");
        TerminalHelper.WriteLine($"  filesystem.allowRead: {(sandbox.Filesystem.AllowRead.Count == 0 ? "（无）" : string.Join(", ", sandbox.Filesystem.AllowRead))}");

        TerminalHelper.WriteLine("使用 /sandbox-toggle help 查看子命令；直接修改 settings.json 后需重启生效。");    }

    /// <summary>帮助文本 — 覆盖全部子命令、只读/写入语义与示例。</summary>
    private static void ShowHelp() {
        TerminalHelper.WriteLine("用法: /sandbox-toggle <子命令>");
        TerminalHelper.WriteLine("  （无参数）         显示设置面板：会话状态 + 当前 settings.json 沙箱值");
        TerminalHelper.WriteLine("  status             查看会话沙箱、设置开关、平台支持与策略锁");
        TerminalHelper.WriteLine("  on|enable          写入 sandbox.enabled=true 并进入会话沙箱");
        TerminalHelper.WriteLine("  off|disable        写入 sandbox.enabled=false 并退出会话沙箱");
        TerminalHelper.WriteLine("  exclude           查看 sandbox.excludedCommands；带模式时给出写回指引");
        TerminalHelper.WriteLine("  switch <type>      切换沙箱提供器：soft|process|docker|bubblewrap");
        TerminalHelper.WriteLine("  help|--help        显示本帮助");
        TerminalHelper.WriteLine("示例: /sandbox-toggle exclude \"npm run test:*\"");
        TerminalHelper.WriteLine("说明: excludedCommands 是安全边界之外的便利项；配置服务只支持标量键，数组需编辑 settings.json。");
    }

    private static async Task HandleSwitchAsync(ISandboxManager? sandboxManager, ChatCommandContext context, string args) {
        if (sandboxManager is null) {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxServiceNotInitialized));
            return;
        }

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            TerminalHelper.WriteLine($"当前沙箱类型: {sandboxManager.ActiveSandboxType.ToValue()}");
            TerminalHelper.WriteLine($"可用类型: {string.Join(", ", sandboxManager.GetAvailableTypes().Select(t => t.ToValue()))}");
            TerminalHelper.WriteLine("用法: /sandbox-toggle switch <soft|process|docker|bubblewrap>");
            return;
        }

        var targetType = SandboxTypeExtensions.FromValue(parts[1]);
        if (targetType is null) {
            TerminalHelper.WriteLine($"未知沙箱类型: {parts[1]}");
            TerminalHelper.WriteLine($"可用类型: {string.Join(", ", sandboxManager.GetAvailableTypes().Select(t => t.ToValue()))}");
            return;
        }

        try {
            await sandboxManager.SwitchProviderAsync(targetType.Value, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"沙箱已切换到: {targetType.Value.ToValue()}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("切换沙箱类型", ex);
        }
    }

    /// <summary>
    /// 查看/提示命令排除清单 — 对齐基准 <c>sandbox.excludedCommands</c>。
    /// 说明：配置服务只支持标量键，数组字段需直接编辑 settings.json（与基准 settings 同源）。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <param name="args">归一化参数串。</param>
    private static void HandleExclude(ChatCommandContext context, string args) {
        var config = ChatCommandBase.GetService<WorkflowConfig>(context);
        var commands = config?.Sandbox.ExcludedCommands ?? [];

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) {
            TerminalHelper.WriteLine("当前排除命令清单（sandbox.excludedCommands）：");
            if (commands.Count == 0) {
                TerminalHelper.WriteLine(L.T(StringKey.HostSandboxExcludeNone));
            } else {
                foreach (var command in commands) {
                    TerminalHelper.WriteLine($"  {command}");
                }
            }
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("编辑 settings.json 的 current.sandbox.excludedCommands 添加模式（支持 cmd、cmd:*、通配符）；配置服务不支持数组写入。");
            return;
        }

        var pattern = string.Join(' ', parts[1..]).Trim().Trim('"', '\'');
        if (pattern.Length == 0) {
            TerminalHelper.WriteLine("请提供命令模式，例如 /sandbox-toggle exclude \"npm run test:*\"。");
            return;
        }

        TerminalHelper.WriteLine($"命令排除清单只读：请在 settings.json 的 current.sandbox.excludedCommands 中加入 \"{pattern}\"。");
        TerminalHelper.WriteLine("排除命令不进沙箱，也不享受沙箱内免确认——它是安全边界之外的便利项。");
    }

    /// <summary>读取沙箱可写路径白名单 — <c>sandbox.filesystem.allowWrite</c>。</summary>
    private static List<string> GetAllowWritePaths(ChatCommandContext context) {
        var config = ChatCommandBase.GetService<WorkflowConfig>(context);
        return config is null ? [] : [.. config.Sandbox.Filesystem.AllowWrite];
    }
}