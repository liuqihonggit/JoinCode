namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Permissions, Description = "管理权限规则和工作区目录", Usage = "/permissions [list|add|remove|clear|workspace] [args]", Category = ChatCommandCategory.Config)]
public sealed class PermissionsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Permissions;
    public string Description => "管理权限规则和工作区目录";
    public string Usage => "/permissions [list|add|remove|clear|workspace] [args]";
    public string[] Aliases => ["perm"];
    public string ArgumentHint => "[list|add|remove|clear|workspace]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : CrudActionConstants.List;
        var crudAction = CrudActionExtensions.FromValue(action);
        var permAction = PermissionsActionExtensions.FromValue(action);

        switch (crudAction, permAction)
        {
            case (CrudAction.List, _):
            case (CrudAction.Ls, _):
                await ListRulesAsync(context);
                break;
            case (_, PermissionsAction.Add):
                await AddRuleAsync(context, args);
                break;
            case (CrudAction.Remove, _):
            case (CrudAction.Delete, _):
            case (CrudAction.Rm, _):
                await RemoveRuleAsync(context, args);
                break;
            case (_, PermissionsAction.Clear):
                await ClearRulesAsync(context);
                break;
            case (_, PermissionsAction.Workspace):
            case (_, PermissionsAction.Dirs):
            case (_, PermissionsAction.Directories):
                await ManageWorkspaceAsync(context, args);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsUnknownAction, action)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.PermissionsAvailableActions));
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ListRulesAsync(ChatCommandContext context)
    {
        var manager = context.Services.PermissionManager;
        if (manager is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var rules = await manager.ListRulesAsync(context.CancellationToken).ConfigureAwait(false);
        var rulesContent = RenderRulesList(rules);

        var workspaceContent = await RenderWorkspaceContentAsync(context).ConfigureAwait(false);

        var panel = new TabPanel(
            [L.T(StringKey.PermissionsTabRules), L.T(StringKey.PermissionsTabWorkspace)],
            tabIndex => tabIndex switch
            {
                0 => rulesContent,
                1 => workspaceContent,
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private static string RenderRulesList(IReadOnlyList<AgentPermissionRule> rules)
    {
        var sb = new StringBuilder();

        if (rules.Count == 0)
        {
            sb.AppendLine(L.T(StringKey.PermissionsNoRules));
        }
        else
        {
            var allowRules = rules.Where(r => r.Mode == PermissionMode.Auto).ToList();
            var askRules = rules.Where(r => r.Mode == PermissionMode.Ask).ToList();
            var denyRules = rules.Where(r => r.Mode == PermissionMode.Deny).ToList();
            var planRules = rules.Where(r => r.Mode == PermissionMode.Plan).ToList();

            if (allowRules.Count > 0)
            {
                sb.AppendLine($"  {L.T(StringKey.PermissionsModeAllowLabel)} ({allowRules.Count})");
                foreach (var rule in allowRules) RenderRule(sb, rule);
            }

            if (askRules.Count > 0)
            {
                sb.AppendLine($"\n  {L.T(StringKey.PermissionsModeAskLabel)} ({askRules.Count})");
                foreach (var rule in askRules) RenderRule(sb, rule);
            }

            if (denyRules.Count > 0)
            {
                sb.AppendLine($"\n  {L.T(StringKey.PermissionsModeDenyLabel)} ({denyRules.Count})");
                foreach (var rule in denyRules) RenderRule(sb, rule);
            }

            if (planRules.Count > 0)
            {
                sb.AppendLine($"\n  {L.T(StringKey.PermissionsModePlanLabel)} ({planRules.Count})");
                foreach (var rule in planRules) RenderRule(sb, rule);
            }
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsAddRuleHint)}{AnsiStyleConstants.Reset}");
        return sb.ToString();
    }

    private static async Task<string> RenderWorkspaceContentAsync(ChatCommandContext context)
    {
        var sb = new StringBuilder();

        var cwd = context.Services.FileSystem.GetCurrentDirectory();
        sb.AppendLine($"  {cwd}{L.T(StringKey.PermissionsWorkspaceCurrentDir)}");

        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is not null)
        {
            var dirs = workspaceService.GetAdditionalDirectories();
            if (dirs.Count > 0)
            {
                sb.AppendLine();
                foreach (var dir in dirs)
                {
                    var exists = context.Services.FileSystem.DirectoryExists(dir);
                    var marker = exists ? "" : L.T(StringKey.PermissionsDirectoryNotFoundSuffix);
                    sb.AppendLine($"  {dir}{marker}");
                }
            }
        }

        var trustManager = ResolveTrustFolderManager(context);
        if (trustManager is not null)
        {
            var trusted = trustManager.GetAllTrustedFolders();
            if (trusted.Count > 0)
            {
                sb.AppendLine(L.T(StringKey.PermissionsTrustedDirsHeader));
                foreach (var dir in trusted)
                {
                    sb.AppendLine($"  {dir}");
                }
            }
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsAddWorkspaceHint)}{AnsiStyleConstants.Reset}");
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{L.T(StringKey.PermissionsRemoveWorkspaceHint)}{AnsiStyleConstants.Reset}");

        await Task.CompletedTask.ConfigureAwait(false);
        return sb.ToString();
    }

    private static void RenderRule(StringBuilder sb, AgentPermissionRule rule)
    {
        var modeColor = rule.Mode switch
        {
            PermissionMode.Auto => TerminalColors.Success,
            PermissionMode.Ask => TerminalColors.Warning,
            PermissionMode.Deny => TerminalColors.Error,
            PermissionMode.Plan => TerminalColors.Primary,
            _ => TerminalColors.Muted
        };

        sb.AppendLine($"{modeColor}    {rule.AgentPattern} {string.Format(L.T(StringKey.PermissionsRulePriorityLabel), rule.Priority, rule.Level)}{AnsiStyleConstants.Reset}");

        if (!string.IsNullOrEmpty(rule.Description))
        {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDescriptionLabel), rule.Description));
        }

        if (rule.AllowedTools?.Count > 0)
        {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleAllowedToolsLabel), string.Join(", ", rule.AllowedTools)));
        }

        if (rule.DeniedTools?.Count > 0)
        {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDeniedToolsLabel), string.Join(", ", rule.DeniedTools)));
        }

        if (rule.AllowedPaths?.Count > 0)
        {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleAllowedPathsLabel), string.Join(", ", rule.AllowedPaths)));
        }

        if (rule.DeniedPaths?.Count > 0)
        {
            sb.AppendLine(string.Format(L.T(StringKey.PermissionsRuleDeniedPathsLabel), string.Join(", ", rule.DeniedPaths)));
        }
    }

    private static async Task AddRuleAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsAddUsage)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsModeHint));
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsAddExample));
            return;
        }

        var manager = context.Services.PermissionManager;
        if (manager is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var agentPattern = args[1];
        var modeStr = args[2].ToLowerInvariant();

        // "allow" 是 CLI 别名，语义等同于 "auto"（PermissionMode.Auto）
        var permBehavior = PermissionBehaviorExtensions.FromValue(modeStr);
        var mode = permBehavior is PermissionBehavior.Allow
            ? PermissionMode.Auto
            : PermissionModeExtensions.FromValue(modeStr);

        if (mode is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsUnknownMode), modeStr)}{AnsiStyleConstants.Reset}");
            return;
        }

        var description = args.Length > 3 ? string.Join(" ", args[3..]).Trim('"', '\'') : string.Empty;

        var rule = new AgentPermissionRule
        {
            AgentPattern = agentPattern,
            Mode = mode.Value,
            Level = mode.Value == PermissionMode.Auto ? PermissionLevel.Execute : PermissionLevel.Read,
            Description = description
        };

        await manager.AddRuleAsync(rule, context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsAdded), agentPattern, mode.Value)}{AnsiStyleConstants.Reset}");
    }

    private static async Task RemoveRuleAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsRemoveUsage)}{AnsiStyleConstants.Reset}");
            return;
        }

        var manager = context.Services.PermissionManager;
        if (manager is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var agentPattern = args[1];
        var removed = await manager.RemoveRuleAsync(agentPattern, context.CancellationToken).ConfigureAwait(false);

        if (removed)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsRemoved), agentPattern)}{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsRuleNotFound), agentPattern));
        }
    }

    private static async Task ClearRulesAsync(ChatCommandContext context)
    {
        var manager = context.Services.PermissionManager;
        if (manager is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.PermissionsManagerUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var confirmed = await Confirmation.ConfirmAsync(L.T(StringKey.PermissionsConfirmClearAll), context.CancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsCancelled));
            return;
        }

        await manager.ClearRulesAsync(context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.PermissionsCleared)}{AnsiStyleConstants.Reset}");
    }

    private static async Task ManageWorkspaceAsync(ChatCommandContext context, string[] args)
    {
        var subAction = args.Length > 1 ? args[1].ToLowerInvariant() : CrudActionConstants.List;
        var subCrudAction = CrudActionExtensions.FromValue(subAction);
        var subPermAction = PermissionsActionExtensions.FromValue(subAction);

        switch (subCrudAction, subPermAction)
        {
            case (CrudAction.List, _):
            case (CrudAction.Ls, _):
            case (_, PermissionsAction.Show):
                {
                    var content = await RenderWorkspaceContentAsync(context).ConfigureAwait(false);
                    TerminalHelper.WriteLine(content);
                    break;
                }
            case (_, PermissionsAction.Add):
                await AddWorkspaceDirectoryAsync(context, args);
                break;
            case (CrudAction.Remove, _):
            case (CrudAction.Delete, _):
            case (CrudAction.Rm, _):
                await RemoveWorkspaceDirectoryAsync(context, args);
                break;
            case (_, PermissionsAction.Clear):
                await ClearWorkspaceDirectoriesAsync(context);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsUnknownWorkspaceAction), subAction)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceAvailableActions));
                break;
        }
    }

    private static Task AddWorkspaceDirectoryAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsWorkspaceAddUsage)}{AnsiStyleConstants.Reset}");
            return Task.CompletedTask;
        }

        var path = args[2];
        if (!context.Services.FileSystem.DirectoryExists(path))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.PermissionsDirNotFound), path)}{AnsiStyleConstants.Reset}");
            return Task.CompletedTask;
        }

        var fullPath = Path.GetFullPath(path);

        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        var added = workspaceService.AddDirectory(fullPath);
        if (added)
        {
            var trustManager = ResolveTrustFolderManager(context);
            trustManager?.Trust(fullPath);

            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsWorkspaceDirAdded), fullPath)}{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsWorkspaceDirAlreadyExists), fullPath));
        }

        return Task.CompletedTask;
    }

    private static Task RemoveWorkspaceDirectoryAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.PermissionsWorkspaceRemoveUsage)}{AnsiStyleConstants.Reset}");
            return Task.CompletedTask;
        }

        var path = Path.GetFullPath(args[2]);

        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        var removed = workspaceService.RemoveDirectory(path);
        if (removed)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.PermissionsWorkspaceDirRemoved), path)}{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.PermissionsWorkspaceDirNotInWorkspace), path));
        }

        return Task.CompletedTask;
    }

    private static Task ClearWorkspaceDirectoriesAsync(ChatCommandContext context)
    {
        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.PermissionsWorkspaceServiceUnavailable));
            return Task.CompletedTask;
        }

        workspaceService.Clear();
        TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.PermissionsWorkspaceCleared)}{AnsiStyleConstants.Reset}");

        return Task.CompletedTask;
    }

    private static ITrustFolderManager? ResolveTrustFolderManager(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
    }
}
