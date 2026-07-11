namespace Core.Prompts;

/// <summary>
/// 默认系统提示词提供者 - 组合所有标准提示词部分
/// </summary>
[Register]
public sealed class DefaultSystemPromptProvider : ISystemPromptProvider
{
    private readonly SystemPromptProviderOptions _options;

    public DefaultSystemPromptProvider(IFileSystem fs, SystemPromptProviderOptions options, IBriefModeService? briefModeService = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(options);

        _options = new SystemPromptProviderOptions
        {
            CustomIntro = options.CustomIntro,
            EnabledTools = options.EnabledTools,
            AdditionalEnvInfo = options.AdditionalEnvInfo,
            ProjectRules = options.ProjectRules,
            ExternalRules = options.ExternalRules,
            FileContext = options.FileContext,
            McpServers = options.McpServers,
            ScratchpadPath = options.ScratchpadPath,
            IsAgentMode = options.IsAgentMode,
            IsCoordinatorMode = options.IsCoordinatorMode,
            LanguagePreference = options.LanguagePreference,
            ModelId = options.ModelId,
            ModelName = options.ModelName,
            Version = options.Version,
            BuildTime = options.BuildTime,
            IssuesExplainer = options.IssuesExplainer,
            FeedbackChannel = options.FeedbackChannel,
            IsReplMode = options.IsReplMode,
            HasTodoTool = options.HasTodoTool,
            HasTaskTool = options.HasTaskTool,
            HasTeamTools = options.HasTeamTools,
            HasSendMessage = options.HasSendMessage,
            EnableNumericLength = options.EnableNumericLength,
            HasTokenBudget = options.HasTokenBudget,
            IsGitWorktree = options.IsGitWorktree,
            AdditionalWorkdirs = options.AdditionalWorkdirs,
            AwaySummary = options.AwaySummary,
            DailyLogPromptBuilder = options.DailyLogPromptBuilder,
            SearchHistoryPromptBuilder = options.SearchHistoryPromptBuilder,
            AgentDefinitions = options.AgentDefinitions,
            BriefModeService = options.BriefModeService ?? briefModeService,
            FileSystem = options.FileSystem ?? fs,
        };
    }

    public IEnumerable<SystemPromptSection> GetSections()
    {
        PromptConfigSnapshot.SetCurrent(_options);

        foreach (var section in PromptSectionRegistration.GetAlwaysSections())
            yield return section;

        if (_options.IsAgentMode)
        {
            foreach (var section in PromptSectionRegistration.GetAgentModeSections())
                yield return section;
        }

        if (_options.IsCoordinatorMode)
        {
            foreach (var section in PromptSectionRegistration.GetCoordinatorModeSections())
                yield return section;
        }
    }
}
