
namespace Core.Permission;

public static class PermissionDependencyInjectionExtensions
{
    public static IServiceCollection AddPermissionServices(this IServiceCollection services)
    {
        services.AddOptions<PermissionConfig>()
            .Configure<IFileSystem>((options, fs) => {
                var defaultConfig = PermissionConfig.CreateDefault();
                options.AutoApprovedTools = defaultConfig.AutoApprovedTools;
                options.AutoRejectedTools = defaultConfig.AutoRejectedTools;
                options.DangerousOperationPatterns = defaultConfig.DangerousOperationPatterns;
                options.WriteOperationPatterns = defaultConfig.WriteOperationPatterns;
                options.ReadOperationPatterns = defaultConfig.ReadOperationPatterns;
                options.ShellOperationPatterns = defaultConfig.ShellOperationPatterns;
                options.SensitivePathPatterns = defaultConfig.SensitivePathPatterns;
                options.DangerousCommandPatterns = defaultConfig.DangerousCommandPatterns;

                // 从 settings.json 加载 permissions.allow/deny/ask — 对齐 TS 版 loadPermissionsFromSettings
                LoadPermissionsFromSettings(options, fs);
            });

        // 权限检查管道 — 由 [RegisterMiddleware] + 生成器自动注册
        // IPathPermissionChecker — [Register] 自动注册（PathPermissionChecker）

        return services;
    }

    /// <summary>
    /// 从 settings.json 加载 permissions.allow/deny/ask 规则到 PermissionConfig
    /// 对齐 TS 版 loadPermissionsFromSettings — 启动时读取持久化的权限规则
    /// 格式: "WebFetch(domain:example.com)" — 解析为 ToolPermissionRule { ToolName, RuleContent }
    /// </summary>
    private static void LoadPermissionsFromSettings(PermissionConfig options, IFileSystem fs)
    {
        try
        {
            // 使用同步加载，避免 Configure 回调中 .GetAwaiter().GetResult() 死锁
            var settings = SettingsLoader.LoadUserSettings(fs);
            if (settings?.Permissions is null)
                return;

            // 加载 allow 规则
            if (settings.Permissions.Allow is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Allow)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null && !options.AutoApprovedTools.Any(r =>
                        string.Equals(r.ToolName, parsed.ToolName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RuleContent ?? "", parsed.RuleContent ?? "", StringComparison.OrdinalIgnoreCase)))
                    {
                        options.AutoApprovedTools.Add(parsed);
                    }
                }
            }

            // 加载 deny 规则
            if (settings.Permissions.Deny is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Deny)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AutoRejectedTools.Add(parsed);
                }
            }

            // 加载 ask 规则
            if (settings.Permissions.Ask is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Ask)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AskRules.Add(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            // settings.json 不存在或解析失败，使用默认配置
            System.Diagnostics.Trace.WriteLine($"Failed to load permission settings: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析权限规则值 — 对齐 TS 版 PermissionRuleValue 格式
    /// "WebFetch(domain:example.com)" → ToolPermissionRule { ToolName="WebFetch", RuleContent="domain:example.com" }
    /// "Bash" → ToolPermissionRule { ToolName="Bash", RuleContent=null }
    /// </summary>
    private static ToolPermissionRule? ParsePermissionRuleValue(string ruleValue)
    {
        if (string.IsNullOrEmpty(ruleValue))
            return null;

        // 检查是否有括号包裹的 RuleContent
        var parenIndex = ruleValue.IndexOf('(');
        if (parenIndex > 0 && ruleValue.EndsWith(')'))
        {
            var toolName = ruleValue[..parenIndex];
            var ruleContent = ruleValue.Substring(parenIndex + 1, ruleValue.Length - parenIndex - 2);
            return new ToolPermissionRule
            {
                ToolName = toolName,
                RuleContent = ruleContent,
                Description = $"From settings.json: {ruleValue}"
            };
        }

        return new ToolPermissionRule
        {
            ToolName = ruleValue,
            Description = $"From settings.json: {ruleValue}"
        };
    }
}
