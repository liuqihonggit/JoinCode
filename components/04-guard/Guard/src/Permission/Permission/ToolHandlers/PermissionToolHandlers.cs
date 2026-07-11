
namespace Core.Permission.ToolHandlers;

/// <summary>
/// 权限管理工具处理器
/// </summary>
[McpToolHandler(ToolCategory.Permission, Optional = true)]
public class PermissionToolHandlers
{
    private readonly IAgentPermissionManager _permissionManager;
    private static readonly char[] CommaSeparator = [','];

    public PermissionToolHandlers(IAgentPermissionManager permissionManager)
    {
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
    }

    /// <summary>
    /// 添加权限规则
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionAddRule, "添加代理权限规则", "permission")]
    public async Task<ToolResult> PermissionAddRuleAsync(
        [McpToolParameter("代理名称或模式（支持通配符 *）")] string agent_pattern,
        [McpToolParameter("权限模式 (auto/plan/ask/deny)")] string mode,
        [McpToolParameter("权限级别 (none/read/write/execute/admin)", Required = false)] string? level = null,
        [McpToolParameter("允许的工具列表（逗号分隔）", Required = false)] string? allowed_tools = null,
        [McpToolParameter("拒绝的工具列表（逗号分隔）", Required = false)] string? denied_tools = null,
        [McpToolParameter("规则描述", Required = false)] string? description = null,
        [McpToolParameter("优先级（数字越大优先级越高）", Required = false)] int? priority = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_pattern))
        {
            return McpResultBuilder.Error().WithText("agent_pattern 不能为空").Build();
        }

        var permissionMode = PermissionModeExtensions.FromValue(mode);
        if (permissionMode is null)
        {
            return McpResultBuilder.Error()
                .WithText($"无效的权限模式: {mode}。有效值: auto, plan, ask, deny").Build();
        }

        var permissionLevel = PermissionLevel.Read;
        if (!string.IsNullOrEmpty(level))
        {
            var parsedLevel = PermissionLevelExtensions.FromValue(level);
            if (parsedLevel is null)
            {
                return McpResultBuilder.Error()
                    .WithText($"无效的权限级别: {level}。有效值: none, read, write, execute, admin").Build();
            }
            permissionLevel = parsedLevel.Value;
        }

        var rule = new AgentPermissionRule
        {
            AgentPattern = agent_pattern,
            Mode = permissionMode.Value,
            Level = permissionLevel,
            AllowedTools = !string.IsNullOrEmpty(allowed_tools)
                ? allowed_tools.Split(CommaSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList()
                : null,
            DeniedTools = !string.IsNullOrEmpty(denied_tools)
                ? denied_tools.Split(CommaSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList()
                : null,
            Description = description,
            Priority = priority ?? 0
        };

        await _permissionManager.AddRuleAsync(rule, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} 权限规则已添加");
        response.AppendLine();
        response.AppendLine(FormatRule(rule));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 移除权限规则
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionRemoveRule, "Remove a permission rule for an agent", "permission")]
    public async Task<ToolResult> PermissionRemoveRuleAsync(
        [McpToolParameter("代理名称或模式")] string agent_pattern,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_pattern))
        {
            return McpResultBuilder.Error().WithText("agent_pattern 不能为空").Build();
        }

        var removed = await _permissionManager.RemoveRuleAsync(agent_pattern, cancellationToken).ConfigureAwait(false);

        if (!removed)
        {
            return McpResultBuilder.Error().WithText($"未找到规则: {agent_pattern}").Build();
        }

        return McpResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} 规则 '{agent_pattern}' 已移除")
            .Build();
    }

    /// <summary>
    /// 列出所有权限规则
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionListRules, "列出所有权限规则", "permission")]
    public async Task<ToolResult> PermissionListRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var rules = await _permissionManager.ListRulesAsync(cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} 权限规则列表");
        response.AppendLine($"共 {rules.Count} 条规则");
        response.AppendLine();

        if (rules.Count == 0)
        {
            response.AppendLine("暂无权限规则");
        }
        else
        {
            foreach (var rule in rules.OrderByDescending(r => r.Priority))
            {
                response.AppendLine(FormatRule(rule));
                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 检查工具权限
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionCheckTool, "Check an agent's permission for a tool", "permission")]
    public async Task<ToolResult> PermissionCheckToolAsync(
        [McpToolParameter("代理名称")] string agent_name,
        [McpToolParameter("工具名称")] string tool_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_name))
        {
            return McpResultBuilder.Error().WithText("agent_name 不能为空").Build();
        }

        if (string.IsNullOrWhiteSpace(tool_name))
        {
            return McpResultBuilder.Error().WithText("tool_name 不能为空").Build();
        }

        var result = await _permissionManager.CheckToolPermissionAsync(agent_name, tool_name, null, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} 权限检查结果");
        response.AppendLine();
        response.AppendLine($"代理: {agent_name}");
        response.AppendLine($"工具: {tool_name}");
        response.AppendLine($"是否允许: {(result.IsAllowed ? $"{StatusSymbol.Tick.ToValue()} 是" : $"{StatusSymbol.Cross.ToValue()} 否")}");
        response.AppendLine($"权限模式: {result.Mode}");

        if (result.MatchedRule != null)
        {
            response.AppendLine($"匹配规则: {result.MatchedRule.AgentPattern}");
        }

        if (!string.IsNullOrEmpty(result.Reason))
        {
            response.AppendLine($"原因: {result.Reason}");
        }

        if (result.RequiresConfirmation)
        {
            response.AppendLine($"{StatusSymbol.Warning.ToValue()} 需要用户确认");
        }

        if (result.RequiresPlan)
        {
            response.AppendLine($"{ObjectSymbol.Pencil.ToValue()} 需要详细计划");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 检查路径权限
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionCheckPath, "检查代理对路径的权限", "permission")]
    public async Task<ToolResult> PermissionCheckPathAsync(
        [McpToolParameter("代理名称")] string agent_name,
        [McpToolParameter("路径")] string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_name))
        {
            return McpResultBuilder.Error().WithText("agent_name 不能为空").Build();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResultBuilder.Error().WithText("path 不能为空").Build();
        }

        var result = await _permissionManager.CheckPathPermissionAsync(agent_name, path, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} 路径权限检查");
        response.AppendLine();
        response.AppendLine($"代理: {agent_name}");
        response.AppendLine($"路径: {path}");
        response.AppendLine($"是否允许: {(result.IsAllowed ? $"{StatusSymbol.Tick.ToValue()} 是" : $"{StatusSymbol.Cross.ToValue()} 否")}");
        response.AppendLine($"权限模式: {result.Mode}");

        if (result.MatchedRule != null)
        {
            response.AppendLine($"匹配规则: {result.MatchedRule.AgentPattern}");
        }

        if (!string.IsNullOrEmpty(result.Reason))
        {
            response.AppendLine($"原因: {result.Reason}");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取代理权限规则
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionGetAgentRule, "获取指定代理的权限规则", "permission")]
    public async Task<ToolResult> PermissionGetAgentRuleAsync(
        [McpToolParameter("代理名称")] string agent_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_name))
        {
            return McpResultBuilder.Error().WithText("agent_name 不能为空").Build();
        }

        var rule = await _permissionManager.GetRuleForAgentAsync(agent_name, cancellationToken).ConfigureAwait(false);

        if (rule == null)
        {
            return McpResultBuilder.Success()
                .WithText($"代理 '{agent_name}' 没有特定的权限规则，将使用默认设置")
                .Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} 代理 '{agent_name}' 的权限规则");
        response.AppendLine();
        response.AppendLine(FormatRule(rule));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 清除所有权限规则
    /// </summary>
    [McpTool(InteractionToolNameConstants.PermissionClearRules, "Clear all permission rules", "permission")]
    public async Task<ToolResult> PermissionClearRulesAsync(
        [McpToolParameter("确认清除（输入 'yes' 确认）")] string confirm,
        CancellationToken cancellationToken = default)
    {
        if (confirm != "yes")
        {
            return McpResultBuilder.Error()
                .WithText("请输入 'yes' 确认清除所有规则")
                .Build();
        }

        await _permissionManager.ClearRulesAsync(cancellationToken).ConfigureAwait(false);

        return McpResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} 所有权限规则已清除")
            .Build();
    }

    #region Private Methods

    private static string FormatRule(AgentPermissionRule rule)
    {
        var sb = new System.Text.StringBuilder();

        var modeIcon = rule.Mode switch
        {
            PermissionMode.Auto => ObjectSymbol.Lightning.ToValue(),
            PermissionMode.Plan => ObjectSymbol.Pencil.ToValue(),
            PermissionMode.Ask => StatusSymbol.Circle.ToValue(),
            PermissionMode.Deny => StatusSymbol.Prohibited.ToValue(),
            _ => StatusSymbol.Circle.ToValue()
        };

        sb.AppendLine($"{modeIcon} [{rule.Priority}] {rule.AgentPattern}");
        sb.AppendLine($"   模式: {rule.Mode} | 级别: {rule.Level}");

        if (!string.IsNullOrEmpty(rule.Description))
        {
            sb.AppendLine($"   描述: {rule.Description}");
        }

        if (rule.AllowedTools?.Count > 0)
        {
            sb.AppendLine($"   允许工具: {string.Join(", ", rule.AllowedTools)}");
        }

        if (rule.DeniedTools?.Count > 0)
        {
            sb.AppendLine($"   拒绝工具: {string.Join(", ", rule.DeniedTools)}");
        }

        if (rule.AllowedPaths?.Count > 0)
        {
            sb.AppendLine($"   允许路径: {string.Join(", ", rule.AllowedPaths)}");
        }

        if (rule.DeniedPaths?.Count > 0)
        {
            sb.AppendLine($"   拒绝路径: {string.Join(", ", rule.DeniedPaths)}");
        }

        return sb.ToString();
    }

    #endregion
}
