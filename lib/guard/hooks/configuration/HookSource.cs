
namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子配置来源
/// </summary>
public enum HookSource
{
    /// <summary>用户设置 (~/.jcc/settings.json)</summary>
    [EnumValue("userSettings")] UserSettings,

    /// <summary>项目设置 (.jcc/settings.json)</summary>
    [EnumValue("projectSettings")] ProjectSettings,

    /// <summary>本地设置 (.jcc/settings.local.json)</summary>
    [EnumValue("localSettings")] LocalSettings,

    /// <summary>策略设置（托管策略）</summary>
    [EnumValue("policySettings")] PolicySettings,

    /// <summary>插件钩子</summary>
    [EnumValue("pluginHook")] PluginHook,

    /// <summary>会话级临时钩子</summary>
    [EnumValue("sessionHook")] SessionHook,

    /// <summary>内置钩子</summary>
    [EnumValue("builtinHook")] BuiltinHook
}

/// <summary>
/// HookSource 扩展方法
/// </summary>
public static class HookSourceDisplayExtensions
{
    /// <summary>
    /// 获取来源的显示描述
    /// </summary>
    public static string GetDescription(this HookSource source)
    {
        var folder = AppDataConstants.AppDataFolder;
        return source switch
        {
            HookSource.UserSettings => $"User settings (~/{folder}/settings.json)",
            HookSource.ProjectSettings => $"Project settings ({folder}/settings.json)",
            HookSource.LocalSettings => $"Local settings ({folder}/settings.local.json)",
            HookSource.PolicySettings => "Policy settings (managed policy)",
            HookSource.PluginHook => $"Plugin hooks (~/{folder}/plugins/*/hooks/hooks.json)",
            HookSource.SessionHook => "Session hooks (in-memory, temporary)",
            HookSource.BuiltinHook => "Built-in hooks (registered internally)",
            _ => source.ToString()
        };
    }

    /// <summary>
    /// 获取来源的标题显示
    /// </summary>
    public static string GetHeader(this HookSource source)
    {
        return source switch
        {
            HookSource.UserSettings => "User Settings",
            HookSource.ProjectSettings => "Project Settings",
            HookSource.LocalSettings => "Local Settings",
            HookSource.PolicySettings => "Policy Settings",
            HookSource.PluginHook => "Plugin Hooks",
            HookSource.SessionHook => "Session Hooks",
            HookSource.BuiltinHook => "Built-in Hooks",
            _ => source.ToString()
        };
    }

    /// <summary>
    /// 获取来源的内联显示（简短）
    /// </summary>
    public static string GetInlineDisplay(this HookSource source)
    {
        return source switch
        {
            HookSource.UserSettings => "User",
            HookSource.ProjectSettings => "Project",
            HookSource.LocalSettings => "Local",
            HookSource.PolicySettings => "Policy",
            HookSource.PluginHook => "Plugin",
            HookSource.SessionHook => "Session",
            HookSource.BuiltinHook => "Built-in",
            _ => source.ToString()
        };
    }

    /// <summary>
    /// 获取来源的优先级（数值越小优先级越高）
    /// </summary>
    public static int GetPriority(this HookSource source)
    {
        return source switch
        {
            HookSource.UserSettings => 0,
            HookSource.ProjectSettings => 1,
            HookSource.LocalSettings => 2,
            HookSource.PolicySettings => 3,
            HookSource.SessionHook => 4,
            HookSource.BuiltinHook => 999,
            HookSource.PluginHook => 999,
            _ => 1000
        };
    }

    /// <summary>
    /// 检查来源是否可编辑
    /// </summary>
    public static bool IsEditable(this HookSource source)
    {
        return source is HookSource.UserSettings
            or HookSource.ProjectSettings
            or HookSource.LocalSettings;
    }

    /// <summary>
    /// 检查来源是否为持久化存储
    /// </summary>
    public static bool IsPersistent(this HookSource source)
    {
        return source is HookSource.UserSettings
            or HookSource.ProjectSettings
            or HookSource.LocalSettings
            or HookSource.PolicySettings;
    }
}
