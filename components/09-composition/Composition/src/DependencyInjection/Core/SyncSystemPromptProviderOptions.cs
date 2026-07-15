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
    /// </summary>
    public SyncSystemPromptProviderOptions(
        WorkflowConfig config,
        Core.Prompts.FileContextTracker fileContext,
        IAssistantDailyLogService? dailyLogService = null,
        IMemorySearchHistoryService? searchHistoryService = null,
        IBriefModeService? briefModeService = null,
        BashShellProvider? bashProvider = null,
        PowerShellShellProvider? psProvider = null)
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
        BashVersion = bashProvider?.Version;
        BashPath = bashProvider?.ShellPath;
        PowerShellVersion = psProvider?.Version;
        PowerShellPath = psProvider?.ShellPath;
        PowerShellEdition = psProvider is not null
            ? (psProvider.IsCore ? "core" : "desktop")
            : null;
    }
}
