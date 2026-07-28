namespace JoinCode.Abstractions.Localization;

public static partial class StringKey
{
    // === CodeIndexToolHandlers ===
    public const string QueryCannotBeEmpty = "QueryCannotBeEmpty";
    public const string NoMatchingSymbols = "NoMatchingSymbols";
    public const string FoundSymbolsCount = "FoundSymbolsCount";
    public const string LabelLocation = "LabelLocation";
    public const string LabelParentSymbol = "LabelParentSymbol";
    public const string LabelNamespace = "LabelNamespace";
    public const string MoreResults = "MoreResults";
    public const string SymbolSearchFailed = "SymbolSearchFailed";
    public const string SymbolNameCannotBeEmpty = "SymbolNameCannotBeEmpty";
    public const string SymbolDefinitionNotFound = "SymbolDefinitionNotFound";
    public const string LabelSymbolDefinition = "LabelSymbolDefinition";
    public const string SyncLabelType = "SyncLabelType";
    public const string LabelAccessModifier = "LabelAccessModifier";
    public const string FindDefinitionFailed = "FindDefinitionFailed";
    public const string SymbolReferencesNotFound = "SymbolReferencesNotFound";
    public const string FoundReferencesCount = "FoundReferencesCount";
    public const string LabelLine = "LabelLine";
    public const string FindReferencesFailed = "FindReferencesFailed";
    public const string CallersNotFound = "CallersNotFound";
    public const string CallersOfSymbol = "CallersOfSymbol";
    public const string LabelCallSite = "LabelCallSite";
    public const string FindCallersFailed = "FindCallersFailed";
    public const string CalleesNotFound = "CalleesNotFound";
    public const string CalleesOfSymbol = "CalleesOfSymbol";
    public const string FindCalleesFailed = "FindCalleesFailed";
    public const string FromCannotBeEmpty = "FromCannotBeEmpty";
    public const string ToCannotBeEmpty = "ToCannotBeEmpty";
    public const string CallChainNotFound = "CallChainNotFound";
    public const string CallChainSteps = "CallChainSteps";
    public const string FindCallChainFailed = "FindCallChainFailed";
    public const string ModifyNoImpact = "ModifyNoImpact";
    public const string ImpactScopeOfSymbol = "ImpactScopeOfSymbol";
    public const string ImpactScopeAnalysisFailed = "ImpactScopeAnalysisFailed";
    public const string InheritorsNotFound = "InheritorsNotFound";
    public const string InheritorsOfSymbol = "InheritorsOfSymbol";
    public const string FindInheritorsFailed = "FindInheritorsFailed";
    public const string DependenciesNotFound = "DependenciesNotFound";
    public const string DependenciesOfSymbol = "DependenciesOfSymbol";
    public const string FindDependenciesFailed = "FindDependenciesFailed";
    public const string FilePathCannotBeEmpty = "FilePathCannotBeEmpty";
    public const string ModifyFileNoImpact = "ModifyFileNoImpact";
    public const string AffectedFilesOfModify = "AffectedFilesOfModify";
    public const string AffectedFilesAnalysisFailed = "AffectedFilesAnalysisFailed";
    public const string WorkspaceRootCannotBeEmpty = "WorkspaceRootCannotBeEmpty";
    public const string IndexRebuildComplete = "IndexRebuildComplete";
    public const string UpdatedFiles = "UpdatedFiles";
    public const string SkippedFiles = "SkippedFiles";
    public const string DeletedFiles = "DeletedFiles";
    public const string IndexRebuildFailed = "IndexRebuildFailed";
    public const string CodeIndexStats = "CodeIndexStats";
    public const string StatsFileCount = "StatsFileCount";
    public const string StatsSymbolCount = "StatsSymbolCount";
    public const string StatsCallEdgeCount = "StatsCallEdgeCount";
    public const string StatsDependencyEdgeCount = "StatsDependencyEdgeCount";
    public const string StatsProjectCount = "StatsProjectCount";
    public const string StatsLastUpdated = "StatsLastUpdated";
    public const string GetStatsFailed = "GetStatsFailed";
    public const string ProgressiveDisclosureNotEnabled = "ProgressiveDisclosureNotEnabled";
    public const string DisclosureLevelInfo = "DisclosureLevelInfo";
    public const string NeedMoreInfoHint = "NeedMoreInfoHint";
    public const string ProgressiveExploreFailed = "ProgressiveExploreFailed";
    public const string ProjectPathCannotBeEmpty = "ProjectPathCannotBeEmpty";
    public const string ProjectNoDependencies = "ProjectNoDependencies";
    public const string ProjectDependenciesOf = "ProjectDependenciesOf";
    public const string FindProjectDependenciesFailed = "FindProjectDependenciesFailed";
    public const string NoProjectDependsOn = "NoProjectDependsOn";
    public const string ProjectDependentsOf = "ProjectDependentsOf";
    public const string FindProjectDependentsFailed = "FindProjectDependentsFailed";
    public const string ModifyFileNoProjectImpact = "ModifyFileNoProjectImpact";
    public const string AffectedProjectsOfModify = "AffectedProjectsOfModify";
    public const string AffectedProjectsAnalysisFailed = "AffectedProjectsAnalysisFailed";
    public const string ProjectNoNuGetPackages = "ProjectNoNuGetPackages";
    public const string ProjectNuGetPackages = "ProjectNuGetPackages";
    public const string FindNuGetPackagesFailed = "FindNuGetPackagesFailed";
    public const string PackageNameCannotBeEmpty = "PackageNameCannotBeEmpty";
    public const string NoProjectUsingNuGet = "NoProjectUsingNuGet";
    public const string ProjectsUsingNuGet = "ProjectsUsingNuGet";
    public const string FindProjectsFailed = "FindProjectsFailed";
    public const string NoIndexedProjects = "NoIndexedProjects";
    public const string WorkspaceProjects = "WorkspaceProjects";
    public const string LabelPath = "LabelPath";
    public const string LabelTargetFramework = "LabelTargetFramework";
    public const string LabelOutputType = "LabelOutputType";
    public const string ListProjectsFailed = "ListProjectsFailed";

    // === LspToolHandlers ===
    public const string LspGotoDefinitionDesc = "LspGotoDefinitionDesc";
    public const string ParamFilePath = "ParamFilePath";
    public const string ParamLine1Based = "ParamLine1Based";
    public const string ParamCharacter1Based = "ParamCharacter1Based";
    public const string FileNotExist = "FileNotExist";
    public const string NoDefinitionFound = "NoDefinitionFound";
    public const string FoundDefinitionsCount = "FoundDefinitionsCount";
    public const string LabelRowCol = "LabelRowCol";
    public const string LspError = "LspError";
    public const string LspFindReferencesDesc = "LspFindReferencesDesc";
    public const string NoReferencesFound = "NoReferencesFound";
    public const string FoundReferencesCountLsp = "FoundReferencesCountLsp";
    public const string LabelRowColShort = "LabelRowColShort";
    public const string LspHoverDesc = "LspHoverDesc";
    public const string NoHoverInfo = "NoHoverInfo";
    public const string HoverInfo = "HoverInfo";
    public const string LspCompletionDesc = "LspCompletionDesc";
    public const string NoCompletionSuggestions = "NoCompletionSuggestions";
    public const string FoundCompletionCount = "FoundCompletionCount";
    public const string LabelDetail = "LabelDetail";
    public const string LabelDocumentation = "LabelDocumentation";
    public const string MoreSuggestions = "MoreSuggestions";
    public const string LspDocumentSymbolsDesc = "LspDocumentSymbolsDesc";
    public const string NoDocumentSymbols = "NoDocumentSymbols";
    public const string DocumentSymbolsList = "DocumentSymbolsList";
    public const string LspWorkspaceSymbolDesc = "LspWorkspaceSymbolDesc";
    public const string ParamSearchQuery = "ParamSearchQuery";
    public const string NoMatchingSymbolsLsp = "NoMatchingSymbolsLsp";
    public const string WorkspaceSymbolResults = "WorkspaceSymbolResults";
    public const string LabelContainer = "LabelContainer";
    public const string MoreResultsLsp = "MoreResultsLsp";
    public const string LspGotoImplementationDesc = "LspGotoImplementationDesc";
    public const string NoImplementationFound = "NoImplementationFound";
    public const string FoundImplementationsCount = "FoundImplementationsCount";
    public const string LspPrepareCallHierarchyDesc = "LspPrepareCallHierarchyDesc";
    public const string NoCallHierarchyInfo = "NoCallHierarchyInfo";
    public const string FoundCallHierarchyItems = "FoundCallHierarchyItems";
    public const string LspIncomingCallsDesc = "LspIncomingCallsDesc";
    public const string NoIncomingCalls = "NoIncomingCalls";
    public const string IncomingCallsOf = "IncomingCallsOf";
    public const string LabelFile = "LabelFile";
    public const string LspOutgoingCallsDesc = "LspOutgoingCallsDesc";
    public const string NoOutgoingCalls = "NoOutgoingCalls";
    public const string OutgoingCallsOf = "OutgoingCallsOf";

    // === CodeGenerationToolHandlers ===
    public const string GenerateCsharpCodeDesc = "GenerateCsharpCodeDesc";
    public const string ParamCodeDescription = "ParamCodeDescription";
    public const string ParamCodeContext = "ParamCodeContext";
    public const string ParamFrameworkVersion = "ParamFrameworkVersion";
    public const string DescriptionCannotBeEmpty = "DescriptionCannotBeEmpty";
    public const string CodeGenerationFailed = "CodeGenerationFailed";
    public const string GenerateUnitTestDesc = "GenerateUnitTestDesc";
    public const string ParamCodeToTest = "ParamCodeToTest";
    public const string ParamTestFramework = "ParamTestFramework";
    public const string ParamTestCount = "ParamTestCount";
    public const string CodeCannotBeEmpty = "CodeCannotBeEmpty";
    public const string UnitTestGenerationFailed = "UnitTestGenerationFailed";
    public const string GenerateApiControllerDesc = "GenerateApiControllerDesc";
    public const string ParamControllerDescription = "ParamControllerDescription";
    public const string ParamModelDefinition = "ParamModelDefinition";
    public const string ParamIncludeCrud = "ParamIncludeCrud";
    public const string ParamIncludeAuth = "ParamIncludeAuth";
    public const string ApiControllerGenerationFailed = "ApiControllerGenerationFailed";

    // === CodeAnalysisToolHandlers ===
    public const string AnalyzeCsharpCodeDesc = "AnalyzeCsharpCodeDesc";
    public const string ParamCodeToAnalyze = "ParamCodeToAnalyze";
    public const string ParamAnalysisFocus = "ParamAnalysisFocus";
    public const string CodeAnalysisFailed = "CodeAnalysisFailed";
    public const string FindBugsDesc = "FindBugsDesc";
    public const string ParamCodeToCheck = "ParamCodeToCheck";
    public const string ParamBugSeverity = "ParamBugSeverity";
    public const string BugFindFailed = "BugFindFailed";
    public const string OptimizeCodeDesc = "OptimizeCodeDesc";
    public const string ParamCodeToOptimize = "ParamCodeToOptimize";
    public const string ParamOptimizeTarget = "ParamOptimizeTarget";
    public const string CodeOptimizationFailed = "CodeOptimizationFailed";
    public const string SecurityAuditDesc = "SecurityAuditDesc";
    public const string ParamCodeToAudit = "ParamCodeToAudit";
    public const string ParamAuditType = "ParamAuditType";
    public const string SecurityAuditFailed = "SecurityAuditFailed";

    // === GoalToolHandlers ===
    public const string GoalToolHandlerDesc = "GoalToolHandlerDesc";
    public const string GetCurrentGoalDesc = "GetCurrentGoalDesc";
    public const string UpdateGoalDesc = "UpdateGoalDesc";

    // === AgentSummaryToolHandlers ===
    public const string AgentSummaryHandlerDesc = "AgentSummaryHandlerDesc";
    public const string GetSystemStatsDesc = "GetSystemStatsDesc";
    public const string SystemAgentStats = "SystemAgentStats";
    public const string LabelTotalAgents = "LabelTotalAgents";
    public const string LabelActiveAgents = "LabelActiveAgents";
    public const string LabelTotalExecutions = "LabelTotalExecutions";
    public const string LabelRunningExecutions = "LabelRunningExecutions";
    public const string LabelTodayExecutions = "LabelTodayExecutions";
    public const string LabelWeekExecutions = "LabelWeekExecutions";
    public const string LabelStatisticsTime = "LabelStatisticsTime";
    public const string GetAllAgentStatsDesc = "GetAllAgentStatsDesc";
    public const string AgentStatsList = "AgentStatsList";
    public const string TotalAgentsCount = "TotalAgentsCount";
    public const string NoAgentRecords = "NoAgentRecords";
    public const string LabelExecCount = "LabelExecCount";
    public const string LabelSuccessRate = "LabelSuccessRate";
    public const string LabelAvgExecTime = "LabelAvgExecTime";
    public const string LabelTotalToolCalls = "LabelTotalToolCalls";
    public const string LabelLastExecution = "LabelLastExecution";
    public const string GetAgentStatsDesc = "GetAgentStatsDesc";
    public const string AgentNameCannotBeEmpty = "AgentNameCannotBeEmpty";
    public const string AgentStatsFor = "AgentStatsFor";
    public const string LabelSuccess = "LabelSuccess";
    public const string LabelFailed = "LabelFailed";
    public const string LabelTotalExecTime = "LabelTotalExecTime";
    public const string LabelAvgExecTimeFor = "LabelAvgExecTimeFor";
    public const string GetAgentHistoryDesc = "GetAgentHistoryDesc";
    public const string AgentHistoryFor = "AgentHistoryFor";
    public const string RecentRecordsCount = "RecentRecordsCount";
    public const string NoExecutionRecords = "NoExecutionRecords";
    public const string LabelTask = "LabelTask";
    public const string SyncLabelStatus = "SyncLabelStatus";
    public const string LabelDuration = "LabelDuration";
    public const string LabelSteps = "LabelSteps";
    public const string LabelToolCalls = "LabelToolCalls";
    public const string GetRunningExecutionsDesc = "GetRunningExecutionsDesc";
    public const string RunningExecutions = "RunningExecutions";
    public const string RunningCount = "RunningCount";
    public const string NoRunningExecutions = "NoRunningExecutions";
    public const string LabelStartTime = "LabelStartTime";
    public const string LabelAlreadyRunning = "LabelAlreadyRunning";
    public const string GetExecutionDetailDesc = "GetExecutionDetailDesc";
    public const string ExecutionIdCannotBeEmpty = "ExecutionIdCannotBeEmpty";
    public const string ExecutionNotFound = "ExecutionNotFound";
    public const string ExecutionDetail = "ExecutionDetail";
    public const string LabelAgent = "LabelAgent";
    public const string LabelCreatedTime = "LabelCreatedTime";
    public const string LabelTaskDescription = "LabelTaskDescription";
    public const string ExecutionMetrics = "ExecutionMetrics";
    public const string LabelEndTime = "LabelEndTime";
    public const string LabelDurationTime = "LabelDurationTime";
    public const string LabelExecSteps = "LabelExecSteps";
    public const string LabelSuccessSteps = "LabelSuccessSteps";
    public const string LabelFailedSteps = "LabelFailedSteps";
    public const string LabelMessagesSent = "LabelMessagesSent";
    public const string LabelMessagesReceived = "LabelMessagesReceived";
    public const string ResultSummary = "ResultSummary";
    public const string ErrorMessage = "ErrorMessage";
    public const string ClearHistoryDesc = "ClearHistoryDesc";
    public const string ConfirmClearHistory = "ConfirmClearHistory";
    public const string HistoryClearedDays = "HistoryClearedDays";
    public const string HistoryClearedAll = "HistoryClearedAll";

    // === PermissionAwareToolExecutor ===
    public const string ToolNotFoundLog = "ToolNotFoundLog";
    public const string ToolParamsMissingLog = "ToolParamsMissingLog";
    public const string ToolExecStartLog = "ToolExecStartLog";
    public const string ToolExecSuccessLog = "ToolExecSuccessLog";
    public const string ToolExecCancelledLog = "ToolExecCancelledLog";
    public const string ToolExecPermissionDeniedLog = "ToolExecPermissionDeniedLog";
    public const string ToolExecNeedsConfirmLog = "ToolExecNeedsConfirmLog";
    public const string ToolExecFailedLog = "ToolExecFailedLog";
    public const string AgentToolLimitDeniedLog = "AgentToolLimitDeniedLog";
    public const string ToolNotAllowedInMode = "ToolNotAllowedInMode";
    public const string AgentToolLimitPassedLog = "AgentToolLimitPassedLog";
    public const string PermissionCheckSkippedLog = "PermissionCheckSkippedLog";
    public const string PermissionCheckStartLog = "PermissionCheckStartLog";
    public const string PermissionCheckPassedLog = "PermissionCheckPassedLog";
    public const string RemotePolicyDeniedLog = "RemotePolicyDeniedLog";
    public const string RemotePolicyDeniedTool = "RemotePolicyDeniedTool";
    public const string FeatureFlagDisabledLog = "FeatureFlagDisabledLog";
    public const string FeatureFlagDisabledTool = "FeatureFlagDisabledTool";
    public const string DefaultValueLabel = "DefaultValueLabel";
    public const string NoDefaultValue = "NoDefaultValue";
    public const string UnknownPropertyMustProvide = "UnknownPropertyMustProvide";
    public const string MissingRequiredParams = "MissingRequiredParams";

    // === ToolCacheManager ===
    public const string ToolCacheManagerDesc = "ToolCacheManagerDesc";

    // === GeneratedToolCategoryProvider ===
    public const string GeneratedToolCategoryProviderDesc = "GeneratedToolCategoryProviderDesc";

    // === McpResultCollapseClassifier ===
    public const string McpResultCollapseClassifierDesc = "McpResultCollapseClassifierDesc";

    // === McpService ===
    public const string McpServiceAlreadyInitializedLog = "McpServiceAlreadyInitializedLog";
    public const string McpServiceRegisteringLog = "McpServiceRegisteringLog";
    public const string McpServiceInitializedLog = "McpServiceInitializedLog";

    // === TeamToolHandlers ===
    public const string TeamCreateDesc = "TeamCreateDesc";
    public const string ParamTeamName = "ParamTeamName";
    public const string ParamTeamDescription = "ParamTeamDescription";
    public const string ParamInitialMembers = "ParamInitialMembers";
    public const string TeamCreateFailed = "TeamCreateFailed";
    public const string TeamCreated = "TeamCreated";
    public const string TeamDeleteDesc = "TeamDeleteDesc";
    public const string ParamTeamId = "ParamTeamId";
    public const string TeamDeleteFailed = "TeamDeleteFailed";
    public const string TeamDeleted = "TeamDeleted";
    public const string TeamGetDesc = "TeamGetDesc";
    public const string TeamNotFound = "TeamNotFound";
    public const string TeamInfo = "TeamInfo";
    public const string TeamListDesc = "TeamListDesc";
    public const string TeamListCount = "TeamListCount";
    public const string NoTeams = "NoTeams";
    public const string TeamAddMemberDesc = "TeamAddMemberDesc";
    public const string ParamAgentId = "ParamAgentId";
    public const string AddMemberFailed = "AddMemberFailed";
    public const string MemberAdded = "MemberAdded";
    public const string TeamRemoveMemberDesc = "TeamRemoveMemberDesc";
    public const string RemoveMemberFailed = "RemoveMemberFailed";
    public const string MemberRemoved = "MemberRemoved";
    public const string TeamSendMessageDesc = "TeamSendMessageDesc";
    public const string ParamSenderId = "ParamSenderId";
    public const string ParamMessageContent = "ParamMessageContent";
    public const string ParamMessageType = "ParamMessageType";
    public const string SendMessageFailed = "SendMessageFailed";
    public const string MessageSentToTeam = "MessageSentToTeam";
    public const string TeamDirectMessageDesc = "TeamDirectMessageDesc";
    public const string ParamTargetAgentId = "ParamTargetAgentId";
    public const string DirectMessageFailed = "DirectMessageFailed";
    public const string DirectMessageSent = "DirectMessageSent";
    public const string TeamBroadcastDesc = "TeamBroadcastDesc";
    public const string BroadcastFailed = "BroadcastFailed";
    public const string BroadcastSent = "BroadcastSent";
    public const string TeamGetMessagesDesc = "TeamGetMessagesDesc";
    public const string ParamMessageLimit = "ParamMessageLimit";
    public const string TeamMessageHistory = "TeamMessageHistory";
    public const string MessageCount = "MessageCount";
    public const string NoMessages = "NoMessages";
    public const string TeamNameCannotBeEmpty = "TeamNameCannotBeEmpty";
    public const string TeamIdCannotBeEmpty = "TeamIdCannotBeEmpty";
    public const string AgentIdCannotBeEmpty = "AgentIdCannotBeEmpty";
    public const string SenderIdCannotBeEmpty = "SenderIdCannotBeEmpty";
    public const string ContentCannotBeEmpty = "ContentCannotBeEmpty";
    public const string TargetAgentIdCannotBeEmpty = "TargetAgentIdCannotBeEmpty";
    public const string LabelTeamName = "LabelTeamName";
    public const string LabelTeamDescription = "LabelTeamDescription";
    public const string LabelMemberCount = "LabelMemberCount";
    public const string LabelMembers = "LabelMembers";
    public const string TeamLabelCreatedTime = "TeamLabelCreatedTime";
    public const string LabelLastActivity = "LabelLastActivity";
    public const string TeamSummaryFormat = "TeamSummaryFormat";

    // === BuiltInAgentToolHandlers ===
    public const string PlanAgentDesc = "PlanAgentDesc";
    public const string ParamGoal = "ParamGoal";
    public const string ParamContext = "ParamContext";
    public const string ParamConstraints = "ParamConstraints";
    public const string PlanAgentCalledLog = "PlanAgentCalledLog";
    public const string PlanCreationFailed = "PlanCreationFailed";
    public const string PlanAgentErrorLog = "PlanAgentErrorLog";
    public const string AgentCallFailed = "AgentCallFailed";
    public const string ExploreAgentDesc = "ExploreAgentDesc";
    public const string ParamTargetPath = "ParamTargetPath";
    public const string ParamFocusArea = "ParamFocusArea";
    public const string ParamExploreDepth = "ParamExploreDepth";
    public const string ExploreAgentCalledLog = "ExploreAgentCalledLog";
    public const string ExploreFailed = "ExploreFailed";
    public const string ExploreAgentErrorLog = "ExploreAgentErrorLog";
    public const string VerificationAgentDesc = "VerificationAgentDesc";
    public const string ParamCode = "ParamCode";
    public const string ParamLanguage = "ParamLanguage";
    public const string ParamVerifyAspect = "ParamVerifyAspect";
    public const string VerificationAgentCalledLog = "VerificationAgentCalledLog";
    public const string VerificationAspectFailed = "VerificationAspectFailed";
    public const string VerificationFailed = "VerificationFailed";
    public const string VerificationAgentErrorLog = "VerificationAgentErrorLog";

    // === AgentDefinitionProvider ===
    public const string AgentDefinitionLoadedLog = "AgentDefinitionLoadedLog";
    public const string ReadAgentDefinitionFailedLog = "ReadAgentDefinitionFailedLog";
    public const string ScanAgentDefinitionFailedLog = "ScanAgentDefinitionFailedLog";
    public const string CustomAgentWhenToUse = "CustomAgentWhenToUse";

    // === ServiceRegistration.CoreServices ===
    public const string FileOperationConfigValidationFailed = "FileOperationConfigValidationFailed";
    public const string ShellExecutionConfigValidationFailed = "ShellExecutionConfigValidationFailed";

    // === ServiceRegistration.Skills ===
    public const string RegisterPluginSkillFailedLog = "RegisterPluginSkillFailedLog";

    // === TeamMemorySyncHostedService ===
    public const string TeamMemorySyncStartedLog = "TeamMemorySyncStartedLog";
    public const string TeamMemorySyncStartFailedLog = "TeamMemorySyncStartFailedLog";
    public const string TeamMemorySyncStoppedLog = "TeamMemorySyncStoppedLog";
    public const string TeamMemorySyncStopFailedLog = "TeamMemorySyncStopFailedLog";

    // === McpToolSyncBridge ===
    public const string McpToolSyncUpdatedLog = "McpToolSyncUpdatedLog";
    public const string McpToolSyncFailedLog = "McpToolSyncFailedLog";
    public const string McpResourceSyncMessage = "McpResourceSyncMessage";
    public const string McpResourceSyncUpdatedLog = "McpResourceSyncUpdatedLog";
    public const string McpResourceSyncFailedLog = "McpResourceSyncFailedLog";
    public const string McpPromptSyncMessage = "McpPromptSyncMessage";
    public const string McpPromptSyncUpdatedLog = "McpPromptSyncUpdatedLog";
    public const string McpPromptSyncFailedLog = "McpPromptSyncFailedLog";

    // === TaskService ===
    public const string CircularDependencyRejected = "CircularDependencyRejected";
    public const string DependencyAlreadyExists = "DependencyAlreadyExists";
    public const string TaskNoDependencies = "TaskNoDependencies";

    // === InProcessTeammateTask ===
    public const string InProcessTeammateStartLog = "InProcessTeammateStartLog";
    public const string InProcessTeammateFailedLog = "InProcessTeammateFailedLog";
    public const string CleanupTeammateAgentFailedLog = "CleanupTeammateAgentFailedLog";
    public const string CleanupTeammateAttemptFailedLog = "CleanupTeammateAttemptFailedLog";

    // === LocalShellTask ===
    public const string LocalShellTaskStartLog = "LocalShellTaskStartLog";
    public const string CommandTimeout = "CommandTimeout";
    public const string LocalShellTaskFailedLog = "LocalShellTaskFailedLog";
    public const string LocalPowershellTaskStartLog = "LocalPowershellTaskStartLog";
    public const string LocalPowershellTaskFailedLog = "LocalPowershellTaskFailedLog";

    // === HighWaterMarkManager ===
    public const string LockAcquireTimeout = "LockAcquireTimeout";

    // === TaskFileWriter ===
    public const string DeleteTempFileFailedLog = "DeleteTempFileFailedLog";

    // === StructuredTaskMarkdown ===
    public const string TaskHeader = "TaskHeader";
    public const string LabelPriority = "LabelPriority";
    public const string LabelCreatedAt = "LabelCreatedAt";
    public const string LabelWorkScope = "LabelWorkScope";
    public const string LabelParentTask = "LabelParentTask";
    public const string SectionDescription = "SectionDescription";
    public const string StructuredTaskCount = "StructuredTaskCount";
    public const string TaskOrderDescription = "TaskOrderDescription";
    public const string LabelTaskStatus = "LabelTaskStatus";
    public const string LabelTaskResult = "LabelTaskResult";
    public const string LabelPossibilities = "LabelPossibilities";
    public const string ExclusionReasonSuffix = "ExclusionReasonSuffix";

    // === ToolPortingPlanRunner ===
    public const string PortingPlanStartLog = "PortingPlanStartLog";
    public const string StartTimeLog = "StartTimeLog";
    public const string FirstWaveTaskCount = "FirstWaveTaskCount";
    public const string SecondWaveTaskCount = "SecondWaveTaskCount";
    public const string SuggestedAgentCount = "SuggestedAgentCount";
    public const string ExecutionReport = "ExecutionReport";
    public const string TotalTaskCount = "TotalTaskCount";
    public const string CompletedCount = "CompletedCount";
    public const string FailedCount = "FailedCount";
    public const string PendingCount = "PendingCount";
    public const string CompletionRate = "CompletionRate";
    public const string ExecutionDuration = "ExecutionDuration";
    public const string FailedTaskList = "FailedTaskList";
    public const string TaskDetails = "TaskDetails";
    public const string AgentCountLabel = "AgentCountLabel";
    public const string ExecutionResultLabel = "ExecutionResultLabel";
    public const string SuccessLabel = "SuccessLabel";
    public const string FailedLabel = "FailedLabel";
    public const string PortingPlanTitle = "PortingPlanTitle";
    public const string SectionOverview = "SectionOverview";
    public const string TotalTasksLabel = "TotalTasksLabel";
    public const string FirstWaveLabel = "FirstWaveLabel";
    public const string SecondWaveLabel = "SecondWaveLabel";
    public const string SuggestedAgentsLabel = "SuggestedAgentsLabel";
    public const string FirstWaveImmediateStart = "FirstWaveImmediateStart";
    public const string FirstWaveTableHeader = "FirstWaveTableHeader";
    public const string AgentScopeLabel = "AgentScopeLabel";
    public const string SecondWaveConditionalTrigger = "SecondWaveConditionalTrigger";
    public const string SecondWaveTableHeader = "SecondWaveTableHeader";
    public const string SectionExecutionOrder = "SectionExecutionOrder";
    public const string PhaseLabel = "PhaseLabel";
    public const string SectionDependencyGraph = "SectionDependencyGraph";
    public const string FirstWaveParallelStart = "FirstWaveParallelStart";
    public const string IndependentLabel = "IndependentLabel";
    public const string FullTaskScope = "FullTaskScope";
    public const string FirstWaveDescription = "FirstWaveDescription";
    public const string SecondWaveDescription = "SecondWaveDescription";

    // === ToolPortingScheduler ===
    public const string Task01Desc = "Task01Desc";
    public const string Task03Desc = "Task03Desc";
    public const string Task04Desc = "Task04Desc";
    public const string Task05Desc = "Task05Desc";
    public const string Task07Desc = "Task07Desc";
    public const string Task08Desc = "Task08Desc";
    public const string Task09Desc = "Task09Desc";
    public const string Task10Desc = "Task10Desc";
    public const string Task11Desc = "Task11Desc";
    public const string Task02Desc = "Task02Desc";
    public const string Task06Desc = "Task06Desc";
    public const string Task12Desc = "Task12Desc";
    public const string TaskStarted = "TaskStarted";
    public const string TaskCompletedMsg = "TaskCompletedMsg";

    // === FileBasedTaskService ===
    public const string CreateTaskDirLog = "CreateTaskDirLog";
    public const string CreateTaskLog = "CreateTaskLog";
    public const string CreateTaskFailedLog = "CreateTaskFailedLog";
    public const string ListTaskFailedLog = "ListTaskFailedLog";
    public const string TaskNotExist = "TaskNotExist";
    public const string UpdateTaskLog = "UpdateTaskLog";
    public const string UpdateTaskFailedLog = "UpdateTaskFailedLog";
    public const string SetTaskDepFailedLog = "SetTaskDepFailedLog";
    public const string RemoveTaskDepFailedLog = "RemoveTaskDepFailedLog";
    public const string DeleteTaskFileFailedLog = "DeleteTaskFileFailedLog";
    public const string TaskListResetLog = "TaskListResetLog";

    // === TaskExecutor ===
    public const string StartTaskLog = "StartTaskLog";
    public const string TaskCancelledMsg = "TaskCancelledMsg";
    public const string TaskCancelledLog = "TaskCancelledLog";
    public const string TaskExecErrorLog = "TaskExecErrorLog";
    public const string AgentCoordinatorNotInit = "AgentCoordinatorNotInit";
    public const string AgentTaskInstructions = "AgentTaskInstructions";
    public const string CreateSubAgentLog = "CreateSubAgentLog";
    public const string TaskAllAgentsSuccessLog = "TaskAllAgentsSuccessLog";
    public const string TaskPartialSuccessLog = "TaskPartialSuccessLog";
    public const string AllAgentsFailedMsg = "AllAgentsFailedMsg";
    public const string TaskFailedLog = "TaskFailedLog";
    public const string CleanupAgentFailedLog = "CleanupAgentFailedLog";
    public const string SimModeExecLog = "SimModeExecLog";
    public const string SimModeCompleteLog = "SimModeCompleteLog";
    public const string ExecTaskLabel = "ExecTaskLabel";
    public const string TaskDescLabel = "TaskDescLabel";
    public const string AgentIndexLabel = "AgentIndexLabel";
    public const string PriorityLabel = "PriorityLabel";
    public const string DepsLabel = "DepsLabel";
    public const string TaskExecReportTitle = "TaskExecReportTitle";
    public const string ExecTimeLabel = "ExecTimeLabel";
    public const string TotalDurationLabel = "TotalDurationLabel";
    public const string AgentCountLabel2 = "AgentCountLabel2";
    public const string SuccessFailCount = "SuccessFailCount";
    public const string AgentResultHeader = "AgentResultHeader";
    public const string StatusSuccess = "StatusSuccess";
    public const string StatusFailed = "StatusFailed";
    public const string ExecDurationLabel = "ExecDurationLabel";
    public const string OutputLabel = "OutputLabel";
    public const string ErrorLabel = "ErrorLabel";
    public const string MergedOutputTitle = "MergedOutputTitle";
    public const string MultiAgentResultTitle = "MultiAgentResultTitle";
    public const string AgentContributionHeader = "AgentContributionHeader";

    // === ParallelExecutionEngine ===
    public const string SimModeOnlyCtor = "SimModeOnlyCtor";
    public const string StartParallelPlanLog = "StartParallelPlanLog";
    public const string TotalTaskCountLog = "TotalTaskCountLog";
    public const string ExecCancelledLog = "ExecCancelledLog";
    public const string ParallelPlanCompleteLog = "ParallelPlanCompleteLog";
    public const string DepsMetTaskReadyLog = "DepsMetTaskReadyLog";

    // === AgentTaskResult ===
    public const string AgentTaskResultFormat = "AgentTaskResultFormat";

    // === TaskRuntime ===
    public const string RuntimeCreateTaskLog = "RuntimeCreateTaskLog";
    public const string RuntimeTaskNotExist = "RuntimeTaskNotExist";
    public const string RuntimeUpdateTaskLog = "RuntimeUpdateTaskLog";
    public const string DepTaskNotExist = "DepTaskNotExist";
    public const string RuntimeSetDepLog = "RuntimeSetDepLog";
    public const string DepNotExist = "DepNotExist";
    public const string PersistTasksLog = "PersistTasksLog";
    public const string CrashRecoveryMsg = "CrashRecoveryMsg";
    public const string RecoverTasksLog = "RecoverTasksLog";
    public const string RemoteAgentTaskExecutorNotRegistered = "RemoteAgentTaskExecutorNotRegistered";
    public const string WorkflowTaskExecutorNotRegistered = "WorkflowTaskExecutorNotRegistered";
    public const string MonitorMcpTaskExecutorNotRegistered = "MonitorMcpTaskExecutorNotRegistered";
    public const string LocalShellTaskExecutorNotRegistered = "LocalShellTaskExecutorNotRegistered";
    public const string InProcessTeammateTaskExecutorNotRegistered = "InProcessTeammateTaskExecutorNotRegistered";

    // === AgentServices (AgentWorktreeService, AgentServiceImpl, AgentSummaryService, AgentService, AgentPromptBuilder) ===
    public const string AgentWorktreeServiceDesc = "AgentWorktreeServiceDesc";
    public const string AgentServiceImplDesc = "AgentServiceImplDesc";
    public const string AgentSummaryServiceDesc = "AgentSummaryServiceDesc";
    public const string AgentServiceDesc = "AgentServiceDesc";
    public const string AgentPromptBuilderDesc = "AgentPromptBuilderDesc";

    // === AgentSettings, ContextCompressionConfig ===
    public const string AgentSettingsDesc = "AgentSettingsDesc";
    public const string ContextCompressionConfigDesc = "ContextCompressionConfigDesc";

    // === IAgentExecutionEngine, IAgentLifecycleManager ===
    public const string IAgentExecutionEngineDesc = "IAgentExecutionEngineDesc";
    public const string IAgentLifecycleManagerDesc = "IAgentLifecycleManagerDesc";

    // === IBuiltInAgent, BuiltInAgentFactory, BuiltInAgentBase, AgentPrompts ===
    public const string IBuiltInAgentDesc = "IBuiltInAgentDesc";
    public const string BuiltInAgentFactoryDesc = "BuiltInAgentFactoryDesc";
    public const string BuiltInAgentBaseDesc = "BuiltInAgentBaseDesc";
    public const string AgentPromptsDesc = "AgentPromptsDesc";

    // === TeamManager, TeammateInitService ===
    public const string TeamManagerDesc = "TeamManagerDesc";
    public const string TeammateInitServiceDesc = "TeammateInitServiceDesc";

    // === SwarmPermissionMessageRouter, SwarmPermissionRequestProcessor, SwarmPermissionCallbackService ===
    public const string SwarmPermissionMessageRouterDesc = "SwarmPermissionMessageRouterDesc";
    public const string SwarmPermissionRequestProcessorDesc = "SwarmPermissionRequestProcessorDesc";
    public const string SwarmPermissionCallbackServiceDesc = "SwarmPermissionCallbackServiceDesc";

    // === SubAgent, ForkSubAgentManager ===
    public const string SubAgentDesc = "SubAgentDesc";
    public const string ForkSubAgentManagerDesc = "ForkSubAgentManagerDesc";

    // === RetryPolicy ===
    public const string RetryPolicyDesc = "RetryPolicyDesc";

    // === AgentLifecycleManager, AgentMcpServerManager, AgentWorktreeManager ===
    public const string AgentLifecycleManagerDesc = "AgentLifecycleManagerDesc";
    public const string AgentMcpServerManagerDesc = "AgentMcpServerManagerDesc";
    public const string AgentWorktreeManagerDesc = "AgentWorktreeManagerDesc";

    // === AgentStateMachine, CoordinatorReport, AgentCoordinator, AgentCoordinatorConstants, AgentExecutionContext, AgentExecutionEngine ===
    public const string AgentStateMachineDesc = "AgentStateMachineDesc";
    public const string CoordinatorReportDesc = "CoordinatorReportDesc";
    public const string AgentCoordinatorDesc = "AgentCoordinatorDesc";
    public const string AgentCoordinatorConstantsDesc = "AgentCoordinatorConstantsDesc";
    public const string AgentExecutionContextDesc = "AgentExecutionContextDesc";
    public const string AgentExecutionEngineDesc = "AgentExecutionEngineDesc";

    // === BuiltInAgents (PlanAgent, VerificationAgent, ContextCompressionAgent, ExploreAgent, GeneralPurposeAgent, ClaudeCodeGuideAgent) ===
    public const string PlanAgentClassDesc = "PlanAgentClassDesc";
    public const string VerificationAgentClassDesc = "VerificationAgentClassDesc";
    public const string ContextCompressionAgentClassDesc = "ContextCompressionAgentClassDesc";
    public const string ExploreAgentClassDesc = "ExploreAgentClassDesc";
    public const string GeneralPurposeAgentClassDesc = "GeneralPurposeAgentClassDesc";
    public const string ClaudeCodeGuideAgentClassDesc = "ClaudeCodeGuideAgentClassDesc";

    // === BuiltInAgentToolHandlers (additional) ===
    public const string GeneralAgentDesc = "GeneralAgentDesc";
    public const string ParamTask = "ParamTask";
    public const string ParamInput = "ParamInput";
    public const string GeneralAgentCalledLog = "GeneralAgentCalledLog";
    public const string GeneralTaskFailed = "GeneralTaskFailed";
    public const string GeneralAgentErrorLog = "GeneralAgentErrorLog";
    public const string GuideAgentDesc = "GuideAgentDesc";
    public const string ParamQuestion = "ParamQuestion";
    public const string ParamFeature = "ParamFeature";
    public const string GuideAgentCalledLog = "GuideAgentCalledLog";
    public const string GuideFailed = "GuideFailed";
    public const string GuideAgentErrorLog = "GuideAgentErrorLog";
    public const string ListAgentsDesc = "ListAgentsDesc";
    public const string AvailableBuiltInAgentsTitle = "AvailableBuiltInAgentsTitle";
    public const string SyncLabelDescription = "SyncLabelDescription";
    public const string UsageInstructions = "UsageInstructions";
    public const string PlanAgentUsage = "PlanAgentUsage";
    public const string ExploreAgentUsage = "ExploreAgentUsage";
    public const string VerificationAgentUsage = "VerificationAgentUsage";
    public const string GeneralAgentUsage = "GeneralAgentUsage";
    public const string GuideAgentUsage = "GuideAgentUsage";
    public const string PlanGenerated = "PlanGenerated";
    public const string ExploreCompleted = "ExploreCompleted";
    public const string VerificationCompleted = "VerificationCompleted";
    public const string TaskCompleted = "TaskCompleted";
    public const string HelpInfo = "HelpInfo";
    public const string LabelDurationMs = "LabelDurationMs";
    public const string LabelTokenUsage = "LabelTokenUsage";
}
