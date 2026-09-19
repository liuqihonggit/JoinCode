namespace Core.Permission;

/// <summary>
/// 工具权限过滤器接口 — 基于拒绝规则过滤可用工具
/// </summary>
public interface IToolPermissionFilter {
    /// <summary>
    /// 按拒绝规则过滤工具列表
    /// </summary>
    /// <param name="toolNames">待过滤的工具名称列表</param>
    /// <param name="permissionMode">权限模式,可选</param>
    /// <returns>未被拒绝的工具名称列表</returns>
    IReadOnlyList<string> FilterToolsByDenyRules(IReadOnlyList<string> toolNames, string? permissionMode = null);

    /// <summary>
    /// 判断指定工具是否被拒绝
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="permissionMode">权限模式,可选</param>
    /// <returns>被拒绝返回 true,否则返回 false</returns>
    bool IsToolDenied(string toolName, string? permissionMode = null);

    /// <summary>
    /// 添加拒绝规则
    /// </summary>
    /// <param name="rule">拒绝规则</param>
    void AddDenyRule(ToolDenyRule rule);

    /// <summary>
    /// 移除指定名称的拒绝规则
    /// </summary>
    /// <param name="ruleName">规则名称</param>
    void RemoveDenyRule(string ruleName);
}

/// <summary>
/// 工具拒绝规则定义
/// </summary>
public sealed partial class ToolDenyRule {
    /// <summary>
    /// 规则名称,唯一标识
    /// </summary>
    public required string RuleName { get; init; }
    /// <summary>
    /// 工具匹配模式,可为通配符或正则表达式
    /// </summary>
    public required string ToolPattern { get; init; }
    /// <summary>
    /// 适用的权限模式,为空表示对所有模式生效
    /// </summary>
    public string? PermissionMode { get; init; }
    /// <summary>
    /// 拒绝原因说明
    /// </summary>
    public string? Reason { get; init; }
    /// <summary>
    /// 是否使用正则表达式匹配,默认为通配符匹配
    /// </summary>
    public bool IsRegex { get; init; }
}

/// <summary>
/// 工具权限过滤器实现 — 基于拒绝规则过滤可用工具
/// </summary>
[Register(typeof(IToolPermissionFilter), ServiceLifetime.Singleton)]
public sealed partial class ToolPermissionFilter : ServiceEntity, IToolPermissionFilter {
    private readonly ConcurrentDictionary<string, ToolDenyRule> _denyRules;
    private readonly ILogger<ToolPermissionFilter>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造工具权限过滤器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务,可选</param>
    public ToolPermissionFilter(ILogger<ToolPermissionFilter>? logger = null, ITelemetryService? telemetryService = null) {
        _denyRules = new ConcurrentDictionary<string, ToolDenyRule>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FilterToolsByDenyRules(IReadOnlyList<string> toolNames, string? permissionMode = null) {
        var result = new List<string>();

        foreach (var toolName in toolNames) {
            if (!IsToolDenied(toolName, permissionMode)) {
                result.Add(toolName);
            }
        }

        _logger?.LogDebug("[ToolPermissionFilter] 过滤工具: {Total} -> {Allowed} (模式: {Mode})",
            toolNames.Count, result.Count, permissionMode ?? "default");

        _telemetryService?.RecordCount("permission.filter.count", new() { ["denied"] = (toolNames.Count - result.Count).ToString() }, description: "Tool permission filter count");

        return result;
    }

    /// <inheritdoc />
    public bool IsToolDenied(string toolName, string? permissionMode = null) {
        foreach (var rule in _denyRules.Values) {
            if (!string.IsNullOrEmpty(rule.PermissionMode) &&
                !string.Equals(rule.PermissionMode, permissionMode, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (rule.IsRegex) {
                try {
                    if (Regex.IsMatch(toolName, rule.ToolPattern, RegexOptions.IgnoreCase)) {
                        _logger?.LogDebug("[ToolPermissionFilter] 工具 '{Tool}' 被规则 '{Rule}' 拒绝 (正则匹配)",
                            toolName, rule.RuleName);
                        return true;
                    }
                } catch (RegexParseException ex) {
                    _logger?.LogWarning(ex, "[ToolPermissionFilter] 规则 '{Rule}' 的正则表达式无效: {Pattern}",
                        rule.RuleName, rule.ToolPattern);
                }
            } else {
                if (IsWildcardMatch(toolName, rule.ToolPattern)) {
                    _logger?.LogDebug("[ToolPermissionFilter] 工具 '{Tool}' 被规则 '{Rule}' 拒绝 (通配符匹配)",
                        toolName, rule.RuleName);
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void AddDenyRule(ToolDenyRule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _denyRules[rule.RuleName] = rule;
        _logger?.LogInformation("[ToolPermissionFilter] 添加拒绝规则: {RuleName} (模式: {Pattern})",
            rule.RuleName, rule.ToolPattern);
    }

    /// <inheritdoc />
    public void RemoveDenyRule(string ruleName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        if (_denyRules.TryRemove(ruleName, out _)) {
            _logger?.LogInformation("[ToolPermissionFilter] 移除拒绝规则: {RuleName}", ruleName);
        }
    }

    private static bool IsWildcardMatch(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);
}