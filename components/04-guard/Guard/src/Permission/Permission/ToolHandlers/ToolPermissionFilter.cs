namespace Core.Permission;

public interface IToolPermissionFilter
{
    IReadOnlyList<string> FilterToolsByDenyRules(IReadOnlyList<string> toolNames, string? permissionMode = null);
    bool IsToolDenied(string toolName, string? permissionMode = null);
    void AddDenyRule(ToolDenyRule rule);
    void RemoveDenyRule(string ruleName);
}

public sealed partial class ToolDenyRule
{
    public required string RuleName { get; init; }
    public required string ToolPattern { get; init; }
    public string? PermissionMode { get; init; }
    public string? Reason { get; init; }
    public bool IsRegex { get; init; }
}

[Register]
public sealed partial class ToolPermissionFilter : IToolPermissionFilter
{
    private readonly ConcurrentBag<ToolDenyRule> _denyRules;
    [Inject] private readonly ILogger<ToolPermissionFilter>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public ToolPermissionFilter(ILogger<ToolPermissionFilter>? logger = null, ITelemetryService? telemetryService = null)
    {
        _denyRules = new ConcurrentBag<ToolDenyRule>();
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public IReadOnlyList<string> FilterToolsByDenyRules(IReadOnlyList<string> toolNames, string? permissionMode = null)
    {
        var result = new List<string>();

        foreach (var toolName in toolNames)
        {
            if (!IsToolDenied(toolName, permissionMode))
            {
                result.Add(toolName);
            }
        }

        _logger?.LogDebug("[ToolPermissionFilter] 过滤工具: {Total} -> {Allowed} (模式: {Mode})",
            toolNames.Count, result.Count, permissionMode ?? "default");

        _telemetryService?.RecordCount("permission.filter.count", new() { ["denied"] = (toolNames.Count - result.Count).ToString() }, description: "Tool permission filter count");

        return result;
    }

    public bool IsToolDenied(string toolName, string? permissionMode = null)
    {
        foreach (var rule in _denyRules)
        {
            if (!string.IsNullOrEmpty(rule.PermissionMode) &&
                !string.Equals(rule.PermissionMode, permissionMode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.IsRegex)
            {
                try
                {
                    if (Regex.IsMatch(toolName, rule.ToolPattern, RegexOptions.IgnoreCase))
                    {
                        _logger?.LogDebug("[ToolPermissionFilter] 工具 '{Tool}' 被规则 '{Rule}' 拒绝 (正则匹配)",
                            toolName, rule.RuleName);
                        return true;
                    }
                }
                catch (RegexParseException ex)
                {
                    _logger?.LogWarning(ex, "[ToolPermissionFilter] 规则 '{Rule}' 的正则表达式无效: {Pattern}",
                        rule.RuleName, rule.ToolPattern);
                }
            }
            else
            {
                if (IsWildcardMatch(toolName, rule.ToolPattern))
                {
                    _logger?.LogDebug("[ToolPermissionFilter] 工具 '{Tool}' 被规则 '{Rule}' 拒绝 (通配符匹配)",
                        toolName, rule.RuleName);
                    return true;
                }
            }
        }

        return false;
    }

    public void AddDenyRule(ToolDenyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _denyRules.Add(rule);
        _logger?.LogInformation("[ToolPermissionFilter] 添加拒绝规则: {RuleName} (模式: {Pattern})",
            rule.RuleName, rule.ToolPattern);
    }

    public void RemoveDenyRule(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        var rulesToRemove = _denyRules.Where(r => r.RuleName == ruleName).ToList();
        foreach (var rule in rulesToRemove)
        {
            _ = _denyRules.TryTake(out var _);
        }

        if (rulesToRemove.Count > 0)
        {
            _logger?.LogInformation("[ToolPermissionFilter] 移除拒绝规则: {RuleName}", ruleName);
        }
    }

    private static bool IsWildcardMatch(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);
}
