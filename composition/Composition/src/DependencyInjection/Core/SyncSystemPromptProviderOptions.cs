namespace Core.DependencyInjection;

/// <summary>
/// Sync 层的 SystemPromptProviderOptions 子类 — 桥接 [Register] 自动注册
/// <para>从 WorkflowConfig + 可选 DI 服务推导所有提示词配置属性</para>
/// </summary>
[Register]
public sealed partial class SyncSystemPromptProviderOptions : Core.Prompts.SystemPromptProviderOptions
{
    /// <summary>
    /// DI 构造函数 — 从 WorkflowConfig 和可选服务推导所有属性
    /// shellCapabilityProviders: 所有已注册的 ShellCapabilityProvider，新增 ShellType 无需改此代码
    /// </summary>
    public SyncSystemPromptProviderOptions(
        WorkflowConfig config,
        Core.Prompts.FileContextTracker fileContext,
        IFileSystem fs,
        IAssistantDailyLogService? dailyLogService = null,
        IMemorySearchHistoryService? searchHistoryService = null,
        IBriefModeService? briefModeService = null,
        IEnumerable<ShellCapabilityProvider>? shellCapabilityProviders = null)
    {
        ProjectRules = config.ProjectRules;
        ExternalRules = config.ExternalRules.Count > 0
            ? config.ExternalRules.Select(r => new ExternalRuleEntry
            {
                Name = r.Name,
                Content = r.Content,
                SourcePath = r.SourcePath,
                AlwaysApply = r.AlwaysApply,
                Globs = r.Globs,
                Description = r.Description
            }).ToArray()
            : null;
        FileContext = fileContext;
        IsCoordinatorMode = false;
        AgentDefinitions = null;
        HasTeamTools = true;
        HasSendMessage = true;
        DailyLogPromptBuilder = dailyLogService is not null
            ? () => dailyLogService.BuildDailyLogPromptAsync()
            : null;
        SearchHistoryPromptBuilder = searchHistoryService is not null
            ? async (query) => (await searchHistoryService.BuildSearchingPastContextSectionAsync(query).ConfigureAwait(false))?.PromptText ?? string.Empty
            : null;
        AwaySummary = null;

        var capabilityList = shellCapabilityProviders?
            .Select(p => p.GetCapability(fs))
            .ToList() ?? [];

        var capabilities = capabilityList.ToDictionary(c => c.Type);

        ShellInfos = capabilities.Count > 0
            ? capabilityList.ToDictionary(kvp => kvp.Type, kvp => kvp.ToShellInfo())
            : null;

        if (capabilities.TryGetValue(ShellType.Bash, out var bash))
        {
            BashVersion = bash.Version;
            BashPath = bash.ShellPath;
        }

        if (capabilities.TryGetValue(ShellType.PowerShell, out var ps))
        {
            PowerShellVersion = ps.Version;
            PowerShellPath = ps.ShellPath;
            PowerShellEdition = ps.IsPowerShellCore ? "core" : "desktop";
        }

        if (capabilities.TryGetValue(ShellType.Python, out var py))
        {
            PythonVersion = py.Version;
            PythonPath = py.ShellPath;
        }
    }
}
