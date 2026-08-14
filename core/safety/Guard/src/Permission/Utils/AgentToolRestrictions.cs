namespace Core.Utils;

[Register]
public sealed partial class AgentToolRestrictions : ServiceEntity, IAgentToolRestrictions
{
    private readonly FrozenSet<string> _autoAllowed;
    private readonly FrozenSet<string> _planAllowed;
    private readonly FrozenSet<string> _askAllowed;
    private readonly FrozenSet<string> _autoDenied;
    private readonly FrozenSet<string> _planDenied;
    private readonly FrozenSet<string> _askDenied;

    public AgentToolRestrictions(
        IOptions<PermissionConfig>? configOptions = null,
        ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
        var overrides = configOptions?.Value?.ToolOverrides;
        _autoAllowed = MergeWithOverrides(ToolSecuritySets.AutoAllowedTools, overrides, "auto", allow: true);
        _planAllowed = MergeWithOverrides(ToolSecuritySets.PlanAllowedTools, overrides, "plan", allow: true);
        _askAllowed = MergeWithOverrides(ToolSecuritySets.AskAllowedTools, overrides, "ask", allow: true);
        _autoDenied = MergeWithOverrides(ToolSecuritySets.AutoDeniedTools, overrides, "auto", allow: false);
        _planDenied = MergeWithOverrides(ToolSecuritySets.PlanDeniedTools, overrides, "plan", allow: false);
        _askDenied = MergeWithOverrides(ToolSecuritySets.AskDeniedTools, overrides, "ask", allow: false);
    }

    [Inject] private readonly ITelemetryService? _telemetryService;

    public IReadOnlySet<string> GetAllowedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => _autoAllowed,
            PermissionMode.Plan => _planAllowed,
            PermissionMode.Ask => _askAllowed,
            PermissionMode.Bypass => _autoAllowed,
            _ => _autoAllowed
        };
    }

    public IReadOnlySet<string> GetDeniedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => _autoDenied,
            PermissionMode.Plan => _planDenied,
            PermissionMode.Ask => _askDenied,
            PermissionMode.Bypass => FrozenSet<string>.Empty,
            _ => _autoDenied
        };
    }

    public bool IsToolAllowedForMode(string toolName, PermissionMode mode)
    {
        if (mode == PermissionMode.Bypass)
        {
            RecordPermissionCheckMetrics(toolName, mode, true);
            return true;
        }

        var denied = GetDeniedTools(mode);
        if (denied.Contains(toolName))
        {
            RecordPermissionCheckMetrics(toolName, mode, false);
            return false;
        }

        if (denied.Contains("*"))
        {
            RecordPermissionCheckMetrics(toolName, mode, false);
            return false;
        }

        var allowed = GetAllowedTools(mode);
        if (allowed.Count == 0)
        {
            RecordPermissionCheckMetrics(toolName, mode, true);
            return true;
        }

        var allowedResult = allowed.Contains(toolName);
        RecordPermissionCheckMetrics(toolName, mode, allowedResult);
        return allowedResult;
    }

    private void RecordPermissionCheckMetrics(string toolName, PermissionMode mode, bool isAllowed)
        => _telemetryService?.RecordCount("guard.permission.check.count", new() { ["tool"] = toolName, ["mode"] = mode.ToString(), ["allowed"] = isAllowed.ToString() }, description: "Permission check count");

    private static FrozenSet<string> MergeWithOverrides(
        FrozenSet<string> defaults,
        Dictionary<string, ToolOverrideEntry>? overrides,
        string modeKey,
        bool allow)
    {
        if (overrides is null || !overrides.TryGetValue(modeKey, out var entry))
            return defaults;

        var list = allow ? entry.Allow : entry.Deny;
        if (list is null or { Count: 0 })
            return defaults;

        var merged = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
        merged.UnionWith(list);
        return merged.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
