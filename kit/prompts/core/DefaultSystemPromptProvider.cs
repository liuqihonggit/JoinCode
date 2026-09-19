namespace Core.Prompts;

/// <summary>
/// 默认系统提示词提供者 - 组合所有标准提示词部分
/// </summary>
[Register(typeof(ISystemPromptProvider), ServiceLifetime.Singleton)]
public sealed partial class DefaultSystemPromptProvider : ServiceEntity, ISystemPromptProvider {
    private readonly SystemPromptProviderOptions _options;

    /// <summary>
    /// 初始化 <see cref="DefaultSystemPromptProvider"/> 实例。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="options">系统提示词提供者配置选项。</param>
    /// <param name="briefModeService">简短模式服务（可选，已被 options.BriefModeService 覆盖）。</param>
    /// <param name="clock">时钟服务（可选，已被 options.Clock 覆盖）。</param>
    /// <param name="logger">日志器。</param>
    public DefaultSystemPromptProvider(IFileSystem fs, SystemPromptProviderOptions options, IBriefModeService? briefModeService = null, IClockService? clock = null, ILogger<DefaultSystemPromptProvider>? logger = null) {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(options);

        _options = new SystemPromptProviderOptions {
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
            Clock = options.Clock ?? clock ?? SystemClockService.Instance,
            Logger = logger,
        };
    }

    /// <summary>
    /// 获取所有系统提示词部分，按运行模式组合返回。
    /// </summary>
    /// <returns>系统提示词部分的可枚举序列。</returns>
    public IEnumerable<SystemPromptSection> GetSections() {
        using var scope = PromptConfigSnapshot.EnterScope(_options);

        foreach (var section in PromptSectionRegistration.GetAlwaysSections())
            yield return section;

        if (_options.IsAgentMode) {
            foreach (var section in PromptSectionRegistration.GetAgentModeSections())
                yield return section;
        }

        if (_options.IsCoordinatorMode) {
            foreach (var section in PromptSectionRegistration.GetCoordinatorModeSections())
                yield return section;
        }
    }
}