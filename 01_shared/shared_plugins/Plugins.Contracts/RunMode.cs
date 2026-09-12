namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 运行模式 — 对齐 DSH Standard/Code/Minimal/Creator
/// <para>Standard：完整工具集（文件编辑+shell+搜索+技能+规划+子代理+工作流）</para>
/// <para>Code：SDK 编排（多步操作合并为一个程序）</para>
/// <para>Minimal：双工具（shell + str_replace_editor，基准测试用）</para>
/// <para>Creator：自省+插件实验+预设编写</para>
/// </summary>
public enum RunMode
{
    /// <summary>完整工具集</summary>
    Standard,

    /// <summary>SDK 编排</summary>
    Code,

    /// <summary>双工具（基准测试）</summary>
    Minimal,

    /// <summary>自省+插件实验+预设</summary>
    Creator,
}

/// <summary>
/// 运行模式描述 — 定义每个模式的工具集和行为能力
/// </summary>
public sealed record RunModeDescriptor
{
    /// <summary>模式</summary>
    public required RunMode Mode { get; init; }

    /// <summary>显示名</summary>
    public required string DisplayName { get; init; }

    /// <summary>描述</summary>
    public required string Description { get; init; }

    /// <summary>可用工具列表</summary>
    public required string[] AvailableTools { get; init; }

    /// <summary>是否支持子代理</summary>
    public bool SupportsSubAgents { get; init; }

    /// <summary>是否支持工作流</summary>
    public bool SupportsWorkflows { get; init; }

    /// <summary>是否支持运行时自省</summary>
    public bool SupportsRuntimeInspection { get; init; }

    /// <summary>是否支持插件实验</summary>
    public bool SupportsPluginExperiments { get; init; }
}

/// <summary>
/// 运行模式注册表 — 对齐 DSH 多运行模式
/// <para>映射现有 Interactive/NonInteractive/Doctor/Tui/Json/Headless</para>
/// </summary>
public static class RunModeRegistry
{
    /// <summary>Standard：完整工具集</summary>
    public static readonly RunModeDescriptor Standard = new()
    {
        Mode = RunMode.Standard,
        DisplayName = "Standard",
        Description = "完整工具集：文件编辑+shell+搜索+技能+规划+子代理+工作流",
        AvailableTools = new[] { "file_edit", "shell", "search", "skill", "plan", "subagent", "workflow" },
        SupportsSubAgents = true,
        SupportsWorkflows = true,
        SupportsRuntimeInspection = false,
        SupportsPluginExperiments = false,
    };

    /// <summary>Code：SDK 编排</summary>
    public static readonly RunModeDescriptor Code = new()
    {
        Mode = RunMode.Code,
        DisplayName = "Code",
        Description = "SDK 编排：多步操作合并为一个程序",
        AvailableTools = new[] { "file_edit", "shell", "search", "skill", "plan", "subagent", "workflow", "code_sdk" },
        SupportsSubAgents = true,
        SupportsWorkflows = true,
        SupportsRuntimeInspection = false,
        SupportsPluginExperiments = false,
    };

    /// <summary>Minimal：双工具（基准测试）</summary>
    public static readonly RunModeDescriptor Minimal = new()
    {
        Mode = RunMode.Minimal,
        DisplayName = "Minimal",
        Description = "双工具：shell + str_replace_editor，基准测试用",
        AvailableTools = new[] { "shell", "str_replace_editor" },
        SupportsSubAgents = false,
        SupportsWorkflows = false,
        SupportsRuntimeInspection = false,
        SupportsPluginExperiments = false,
    };

    /// <summary>Creator：自省+插件实验+预设</summary>
    public static readonly RunModeDescriptor Creator = new()
    {
        Mode = RunMode.Creator,
        DisplayName = "Creator",
        Description = "自省+插件实验+预设编写",
        AvailableTools = new[] { "file_edit", "shell", "search", "skill", "plan", "subagent", "workflow", "inspect", "plugin_define" },
        SupportsSubAgents = true,
        SupportsWorkflows = true,
        SupportsRuntimeInspection = true,
        SupportsPluginExperiments = true,
    };

    private static readonly FrozenDictionary<RunMode, RunModeDescriptor> All = new Dictionary<RunMode, RunModeDescriptor>
    {
        [RunMode.Standard] = Standard,
        [RunMode.Code] = Code,
        [RunMode.Minimal] = Minimal,
        [RunMode.Creator] = Creator,
    }.ToFrozenDictionary();

    /// <summary>获取模式描述</summary>
    public static RunModeDescriptor Get(RunMode mode) => All[mode];

    /// <summary>列出所有模式</summary>
    public static IReadOnlyCollection<RunModeDescriptor> List() => All.Values;

    /// <summary>模式是否存在</summary>
    public static bool Exists(RunMode mode) => All.ContainsKey(mode);
}
