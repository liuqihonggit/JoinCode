namespace Core.Permission;

/// <summary>
/// 权限规则文件加载器（TASK070 P1-P2/DSG046 — settings.json permissions 规则的唯一加载实现）：
/// 启动加载（ServiceRegistration 构造 PermissionConfig）与运行期热加载（PermissionChecker mtime 检查）
/// 共用同一实现 — 单一数据源，禁止双处维护。解析委托 <see cref="PermissionRuleParser"/>。
/// </summary>
public static class PermissionRulesFileLoader {
    /// <summary>
    /// 从 settings.json 加载 permissions 规则到配置 — allow/deny/ask/toolOverrides 四段；
    /// settings 缺失或 Permissions 段为空时 no-op（保留既有规则）。
    /// </summary>
    /// <param name="options">权限配置（原地更新 AutoApprovedTools/AutoRejectedTools/AskRules/ToolOverrides）。</param>
    /// <param name="fs">文件系统。</param>
    /// <param name="logger">可选日志。</param>
    public static async Task LoadAsync(PermissionConfig options, IFileSystem fs, ILogger? logger = null) {
        try {
            var settings = await SettingsLoader.LoadUserSettings(fs).ConfigureAwait(false);
            if (settings?.Current?.Permissions is null)
                return;

            if (settings.Current.Permissions.Allow is { Count: > 0 }) {
                foreach (var rule in settings.Current.Permissions.Allow) {
                    var parsed = ParseRuleValue(rule);
                    if (parsed is not null && !options.AutoApprovedTools.Values.Any(r =>
                        string.Equals(r.ToolName, parsed.ToolName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RuleContent ?? "", parsed.RuleContent ?? "", StringComparison.OrdinalIgnoreCase))) {
                        options.AutoApprovedTools = options.AutoApprovedTools.SetItem(parsed.ToolName, parsed);
                    }
                }
            }

            if (settings.Current.Permissions.Deny is { Count: > 0 }) {
                foreach (var rule in settings.Current.Permissions.Deny) {
                    var parsed = ParseRuleValue(rule);
                    if (parsed is not null)
                        options.AutoRejectedTools = options.AutoRejectedTools.SetItem(parsed.ToolName, parsed);
                }
            }

            if (settings.Current.Permissions.Ask is { Count: > 0 }) {
                foreach (var rule in settings.Current.Permissions.Ask) {
                    var parsed = ParseRuleValue(rule);
                    if (parsed is not null)
                        options.AskRules.Add(parsed);
                }
            }

            if (settings.Current.Permissions.ToolOverrides is { Count: > 0 }) {
                foreach (var (mode, entry) in settings.Current.Permissions.ToolOverrides) {
                    options.ToolOverrides = options.ToolOverrides.SetItem(mode, entry);
                }
            }
        } catch (Exception ex) {
            logger?.LogWarning(ex, "Failed to load permission settings");
        }
    }

    /// <summary>规则串 → ToolPermissionRule — 委托 <see cref="PermissionRuleParser"/>（TASK070 P1-P2 唯一数据源）。</summary>
    private static ToolPermissionRule? ParseRuleValue(string ruleValue) {
        var parsed = PermissionRuleParser.Parse(ruleValue);
        if (parsed is null)
            return null;

        // 前缀规则保留 ":*" 后缀于 RuleContent（中间件 MatchesRule 以 EndsWith(":*") 识别前缀语义）
        var content = parsed.IsPrefix && parsed.Content is not null ? $"{parsed.Content}:*" : parsed.Content;
        return new ToolPermissionRule {
            ToolName = parsed.ToolName,
            RuleContent = content,
            Description = $"From settings.json: {ruleValue}"
        };
    }
}
