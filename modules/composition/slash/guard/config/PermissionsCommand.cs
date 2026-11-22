namespace JoinCode.ChatCommands;

/// <summary>
/// /permissions 命令 — 管理权限规则和工作区目录
/// 支持列出、添加、移除、清除权限规则,以及管理工作区额外目录
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Permissions, Description = "管理权限规则和工作区目录", Usage = "/permissions [list|add|remove|clear|workspace|pending] [args]", Category = ChatCommandCategory.Config, Aliases = ["perm"], ArgumentHint = "[list|add|remove|clear|workspace|pending]")]
[ChatCommandArg("action", Type = "string", Description = "权限操作", Enum = new[] { "list", "add", "remove", "clear", "workspace", "pending" }, Default = "list")]
[ChatCommandArg("args", Type = "string", Description = "操作特定参数,如 add 的权限规则")]
public sealed class PermissionsCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /permissions 命令 — 根据子命令分发到对应权限或工作区操作
    /// 支持 list/add/remove/clear/workspace 五种操作,默认 list
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>命令执行结果,始终返回 Continue 表示继续会话</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : CrudActionEnumConstants.List;
        var crudAction = CrudActionExtensions.FromValue(action);
        var permAction = PermissionsActionExtensions.FromValue(action);

        switch (crudAction, permAction) {
            case (CrudAction.List, _):
            case (CrudAction.Ls, _):
            await ListRulesAsync(context).ConfigureAwait(false);
            break;
            case (_, PermissionsAction.Add):
            await AddRuleAsync(context, args).ConfigureAwait(false);
            break;
            case (CrudAction.Remove, _):
            case (CrudAction.Delete, _):
            case (CrudAction.Rm, _):
            await RemoveRuleAsync(context, args).ConfigureAwait(false);
            break;
            case (_, PermissionsAction.Clear):
            await ClearRulesAsync(context).ConfigureAwait(false);
            break;
            case (_, PermissionsAction.Workspace):
            case (_, PermissionsAction.Dirs):
            case (_, PermissionsAction.Directories):
            await ManageWorkspaceAsync(context, args).ConfigureAwait(false);
            break;
            case (_, PermissionsAction.Pending):
            await ManagePendingAsync(context, args).ConfigureAwait(false);
            break;
            default:
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsUnknownAction, action)}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsAvailableActions));
            break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ListRulesAsync(ChatCommandContext context) {
        var manager = context.GetCommandServices().PermissionManager;
        if (manager is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var rules = await manager.ListRulesAsync(context.CancellationToken).ConfigureAwait(false);
        var rulesContent = RenderRulesList(rules);

        var workspaceContent = await RenderWorkspaceContentAsync(context).ConfigureAwait(false);

        var panel = new TabPanel(
            [L.T(StringKey.PermissionsTabRules), L.T(StringKey.PermissionsTabWorkspace)],
            tabIndex => tabIndex switch {
                0 => rulesContent,
                1 => workspaceContent,
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private static string RenderRulesList(IReadOnlyList<AgentPermissionRule> rules) {
        var sb = new StringBuilder();

        if (rules.Count == 0) {
            sb.AppendLine(L.T(StringKey.PermissionsNoRules));
        } else {
            var allowRules = rules.Where(r => r.Mode == PermissionMode.Auto).ToList();
            var askRules = rules.Where(r => r.Mode == PermissionMode.Ask).ToList();
            var planRules = rules.Where(r => r.Mode == PermissionMode.Plan).ToList();

            if (allowRules.Count > 0) {
                sb.AppendLine($"  {L.T(StringKey.PermissionsModeAllowLabel)} ({allowRules.Count})");
                foreach (var rule in allowRules) RenderRule(sb, rule);
            }

            if (askRules.Count > 0) {
                sb.AppendLine($"\n  {L.T(StringKey.PermissionsModeAskLabel)} ({askRules.Count})");
                foreach (var rule in askRules) RenderRule(sb, rule);
            }

            if (planRules.Count > 0) {
                sb.AppendLine($"\n  {L.T(StringKey.PermissionsModePlanLabel)} ({planRules.Count})");
                foreach (var rule in planRules) RenderRule(sb, rule);
            }
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsAddRuleHint)}{AnsiStyleEnumConstants.Reset}");
        return sb.ToString();
    }

    private static async Task<string> RenderWorkspaceContentAsync(ChatCommandContext context) {
        var sb = new StringBuilder();

        var cwd = context.GetCommandServices().FileSystem.GetCurrentDirectory();
        sb.AppendLine($"  {cwd}{L.T(StringKey.PermissionsWorkspaceCurrentDir)}");

        var workspaceService = context.GetCommandServices().WorkspaceService;
        if (workspaceService is not null) {
            var dirs = workspaceService.GetAdditionalDirectories();
            if (dirs.Any()) {
                sb.AppendLine();
                foreach (var dir in dirs) {
                    var exists = context.GetCommandServices().FileSystem.DirectoryExists(dir);
                    var marker = exists ? "" : L.T(StringKey.PermissionsDirectoryNotFoundSuffix);
                    sb.AppendLine($"  {dir}{marker}");
                }
            }
        }

        var trustManager = ResolveTrustFolderManager(context);
        if (trustManager is not null) {
            var trusted = trustManager.GetAllTrustedFolders();
            if (trusted.Count > 0) {
                sb.AppendLine(L.T(StringKey.PermissionsTrustedDirsHeader));
                foreach (var dir in trusted) {
                    sb.AppendLine($"  {dir}");
                }
            }
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsAddWorkspaceHint)}{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsRemoveWorkspaceHint)}{AnsiStyleEnumConstants.Reset}");

        await Task.CompletedTask.ConfigureAwait(false);
        return sb.ToString();
    }

    private static void RenderRule(StringBuilder sb, AgentPermissionRule rule) {
        var modeColor = rule.Mode switch {
            PermissionMode.Auto => TerminalColors.Success,
            PermissionMode.Ask => TerminalColors.Warning,
            PermissionMode.Plan => TerminalColors.Primary,
            PermissionMode.Bypass => TerminalColors.Success,
            _ => TerminalColors.Muted
        };

        sb.AppendLine($"{modeColor}    {rule.AgentPattern} {string.Format(L.T(StringKey.PermissionsRulePriorityLabel), rule.Priority, rule.Level)}{AnsiStyleEnumConstants.Reset}");

        if (!string.IsNullOrEmpty(rule.Description)) {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDescriptionLabel), rule.Description));
        }

        if (rule.AllowedTools?.Count > 0) {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleAllowedToolsLabel), string.Join(", ", rule.AllowedTools)));
        }

        if (rule.DeniedTools?.Count > 0) {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDeniedToolsLabel), string.Join(", ", rule.DeniedTools)));
        }

        if (rule.AllowedPaths?.Count > 0) {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleAllowedPathsLabel), string.Join(", ", rule.AllowedPaths)));
        }

        if (rule.DeniedPaths?.Count > 0) {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDeniedPathsLabel), string.Join(", ", rule.DeniedPaths)));
        }
    }

    private static async Task AddRuleAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 3) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsAddUsage)}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsModeHint));
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsAddExample));
            return;
        }

        var manager = context.GetCommandServices().PermissionManager;
        if (manager is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var agentPattern = args[1];
        var modeStr = args[2].ToLowerInvariant();

        // "allow" 是 CLI 别名，语义等同于 "auto"（PermissionMode.Auto）
        var permBehavior = PermissionBehaviorExtensions.FromValue(modeStr);
        var mode = permBehavior is PermissionBehavior.Allow
            ? PermissionMode.Auto
            : PermissionModeExtensions.FromValue(modeStr);

        if (mode is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsUnknownMode), modeStr)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var description = args.Length > 3 ? string.Join(" ", args[3..]).Trim('"', '\'') : string.Empty;

        var rule = new AgentPermissionRule {
            AgentPattern = agentPattern,
            Mode = mode.Value,
            Level = mode.Value == PermissionMode.Auto ? PermissionLevel.Execute : PermissionLevel.Read,
            Description = description
        };

        await manager.AddRuleAsync(rule, context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsAdded), agentPattern, mode.Value)}{AnsiStyleEnumConstants.Reset}");
    }

    private static async Task RemoveRuleAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsRemoveUsage)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var manager = context.GetCommandServices().PermissionManager;
        if (manager is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var agentPattern = args[1];
        var removed = await manager.RemoveRuleAsync(agentPattern, context.CancellationToken).ConfigureAwait(false);

        if (removed) {
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsRemoved), agentPattern)}{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsRuleNotFound), agentPattern));
        }
    }

    /// <summary>
    /// 后台待确认事项 — 查看后台任务因权限确认被 Defer 的票据，并逐条/批量批准或拒绝。
    /// 批准只代表复核通过并把结果通知发起代理，不会自动恢复原调用（原调用已按 Defer 结束）。
    /// </summary>
    /// <param name="context">命令上下文（服务容器提供 <see cref="IPendingConfirmationResolver"/>）。</param>
    /// <param name="args">命令参数（pending [approve|deny|approve-all|deny-all] [id]）。</param>
    private static Task ManagePendingAsync(ChatCommandContext context, string[] args) {
        var resolver = ChatCommandBase.GetService<IPendingConfirmationResolver>(context, typeof(IPendingConfirmationResolver));
        if (resolver is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}待确认收件箱不可用：当前宿主未注册 IPendingConfirmationResolver；后台票据仍保留，可在 GUI 收件箱处理。{AnsiStyleEnumConstants.Reset}");
            return Task.CompletedTask;
        }

        var action = args.Length > 1 ? args[1].ToLowerInvariant() : CrudActionEnumConstants.List;
        switch (action) {
            case "approve":
            case "allow":
                ResolveOne(resolver, args, PendingConfirmationDecision.Approve);
                break;
            case "deny":
            case "reject":
                ResolveOne(resolver, args, PendingConfirmationDecision.Deny);
                break;
            case "approve-all":
            case "allow-all":
                ResolveBatch(resolver, PendingConfirmationDecision.Approve);
                break;
            case "deny-all":
            case "reject-all":
                ResolveBatch(resolver, PendingConfirmationDecision.Deny);
                break;
            case CrudActionEnumConstants.List:
            case "ls":
                RenderPendingList(resolver);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}未知的待确认操作: {action}{AnsiStyleEnumConstants.Reset}");
                TerminalHelper.WriteLine(PendingUsage);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>列出待确认票据；空列表给出“后台任务均未触发确认”的明确结论。</summary>
    private static void RenderPendingList(IPendingConfirmationResolver resolver) {
        var items = resolver.ListPending();
        if (items.Count == 0) {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}当前没有后台待确认事项。{AnsiStyleEnumConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine($"后台待确认事项（{items.Count}）");
        foreach (var item in items) {
            var agent = string.IsNullOrEmpty(item.AgentId) ? "main" : item.AgentId;
            var created = item.CreatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
            TerminalHelper.WriteLine($"  {TerminalColors.Warning}[{item.Id}]{AnsiStyleEnumConstants.Reset} {item.ToolName} · {agent} · {created}");
            TerminalHelper.WriteLine($"    {item.Reason}");
            if (item.Arguments.Count > 0) {
                var preview = string.Join(", ", item.Arguments.Select(pair => $"{pair.Key}={pair.Value.GetRawText()}"));
                TerminalHelper.WriteLine($"    {TerminalColors.Muted}{preview}{AnsiStyleEnumConstants.Reset}");
            }
        }
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}{PendingUsage}{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}批准只代表复核通过，不会自动恢复原调用；代理会收到通知，可在交互模式重试。{AnsiStyleEnumConstants.Reset}");
    }

    /// <summary>结算指定票据；缺 ID 时输出用法，不存在/过期由 resolver 返回 false。</summary>
    private static void ResolveOne(IPendingConfirmationResolver resolver, string[] args, PendingConfirmationDecision decision) {
        if (args.Length < 3) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}缺少票据 ID；用法: /permissions pending approve|deny <id>{AnsiStyleEnumConstants.Reset}");
            RenderPendingList(resolver);
            return;
        }
        var id = args[2];
        if (resolver.Resolve(id, decision)) {
            var verb = decision == PendingConfirmationDecision.Approve ? "已复核通过" : "已拒绝";
            TerminalHelper.WriteLine($"{TerminalColors.Success}票据 {id} {verb}，已通知发起代理。{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}票据 {id} 不存在、已过期或已被处理；请用 /permissions pending 刷新列表。{AnsiStyleEnumConstants.Reset}");
        }
    }

    /// <summary>批量结算当前全部票据。</summary>
    private static void ResolveBatch(IPendingConfirmationResolver resolver, PendingConfirmationDecision decision) {
        var count = resolver.ResolveAll(decision);
        if (count == 0) {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}当前没有可结算的待确认事项。{AnsiStyleEnumConstants.Reset}");
            return;
        }
        var verb = decision == PendingConfirmationDecision.Approve ? "复核通过" : "拒绝";
        TerminalHelper.WriteLine($"{TerminalColors.Success}已批量{verb} {count} 条待确认事项，并通知各自发起代理。{AnsiStyleEnumConstants.Reset}");
    }

    /// <summary>待确认命令用法提示（唯一文案源，渲染与错误分支共用）。</summary>
    private const string PendingUsage = "/permissions pending [list|approve <id>|deny <id>|approve-all|deny-all]";

    /// <summary>
    /// 清空全部权限规则 — 破坏性操作,先经用户确认再执行
    /// <para>确认经 ConfirmRulesOperationAsync:context.Confirm 回调优先（GUI 弹窗/TUI 对话框注入），
    /// null 降级 CLI 终端 y/N（对齐 CommitCommand/LogoutCommand 先例，非交互环境恒拒绝零回退）</para>
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    private static async Task ClearRulesAsync(ChatCommandContext context) {
        var manager = context.GetCommandServices().PermissionManager;
        if (manager is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        if (!await ConfirmRulesOperationAsync(context, StringKey.PermissionsConfirmClearAll).ConfigureAwait(false)) {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsCancelled));
            return;
        }

        await manager.ClearRulesAsync(context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.PermissionsCleared)}{AnsiStyleEnumConstants.Reset}");
    }

    /// <summary>
    /// 确认权限破坏性操作 — context.Confirm 回调优先，null 降级静态 Dialog.ConfirmAsync
    /// <para>与 LogoutCommand.ConfirmLogoutAllAsync 同形态（替换便利）：回调可注入使确认分支可单测触达，
    /// 未注入时保持静态确认行为（非交互环境恒拒绝）零回退</para>
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <param name="messageKey">确认提示的本地化键</param>
    /// <returns>用户确认返回 true，否则返回 false</returns>
    private static async Task<bool> ConfirmRulesOperationAsync(ChatCommandContext context, string messageKey) {
        var message = L.T(messageKey);
        if (context.Confirm is { } confirm)
            return confirm(message);
        return await Confirmation.ConfirmAsync(message, context.CancellationToken).ConfigureAwait(false);
    }

    private static async Task ManageWorkspaceAsync(ChatCommandContext context, string[] args) {
        var subAction = args.Length > 1 ? args[1].ToLowerInvariant() : CrudActionEnumConstants.List;
        var subCrudAction = CrudActionExtensions.FromValue(subAction);
        var subPermAction = PermissionsActionExtensions.FromValue(subAction);

        switch (subCrudAction, subPermAction) {
            case (CrudAction.List, _):
            case (CrudAction.Ls, _):
            case (_, PermissionsAction.Show): {
                var content = await RenderWorkspaceContentAsync(context).ConfigureAwait(false);
                TerminalHelper.WriteLine(content);
                break;
            }
            case (_, PermissionsAction.Add):
            await AddWorkspaceDirectoryAsync(context, args).ConfigureAwait(false);
            break;
            case (CrudAction.Remove, _):
            case (CrudAction.Delete, _):
            case (CrudAction.Rm, _):
            await RemoveWorkspaceDirectoryAsync(context, args).ConfigureAwait(false);
            break;
            case (_, PermissionsAction.Clear):
            await ClearWorkspaceDirectoriesAsync(context).ConfigureAwait(false);
            break;
            default:
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsUnknownWorkspaceAction), subAction)}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceAvailableActions));
            break;
        }
    }

    private static Task AddWorkspaceDirectoryAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 3) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsWorkspaceAddUsage)}{AnsiStyleEnumConstants.Reset}");
            return Task.CompletedTask;
        }

        var path = args[2];
        if (!context.GetCommandServices().FileSystem.DirectoryExists(path)) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsDirNotFound), path)}{AnsiStyleEnumConstants.Reset}");
            return Task.CompletedTask;
        }

        var fullPath = Path.GetFullPath(path);

        var workspaceService = context.GetCommandServices().WorkspaceService;
        if (workspaceService is null) {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        var added = workspaceService.AddDirectory(fullPath);
        if (added) {
            var trustManager = ResolveTrustFolderManager(context);
            trustManager?.Trust(fullPath);

            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsWorkspaceDirAdded), fullPath)}{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsWorkspaceDirAlreadyExists), fullPath));
        }

        return Task.CompletedTask;
    }

    private static Task RemoveWorkspaceDirectoryAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 3) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsWorkspaceRemoveUsage)}{AnsiStyleEnumConstants.Reset}");
            return Task.CompletedTask;
        }

        var path = Path.GetFullPath(args[2]);

        var workspaceService = context.GetCommandServices().WorkspaceService;
        if (workspaceService is null) {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        var removed = workspaceService.RemoveDirectory(path);
        if (removed) {
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsWorkspaceDirRemoved), path)}{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsWorkspaceDirNotInWorkspace), path));
        }

        return Task.CompletedTask;
    }

    private static Task ClearWorkspaceDirectoriesAsync(ChatCommandContext context) {
        var workspaceService = context.GetCommandServices().WorkspaceService;
        if (workspaceService is null) {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        workspaceService.Clear();
        TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.PermissionsWorkspaceCleared)}{AnsiStyleEnumConstants.Reset}");

        return Task.CompletedTask;
    }

    private static ITrustFolderManager? ResolveTrustFolderManager(ChatCommandContext context) {
        return ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
    }
}