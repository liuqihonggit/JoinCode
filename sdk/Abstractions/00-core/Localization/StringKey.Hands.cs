namespace JoinCode.Abstractions.Localization;

public static partial class StringKey
{
    // === NotebookToolHandlers ===
    public const string NotebookFilePathCannotBeEmpty = "NotebookFilePathCannotBeEmpty";
    public const string NotebookFileAlreadyExists = "NotebookFileAlreadyExists";
    public const string NotebookSaveFailed = "NotebookSaveFailed";
    public const string NotebookCreatedSuccess = "NotebookCreatedSuccess";
    public const string NotebookPathLabel = "NotebookPathLabel";
    public const string NotebookFormatVersion = "NotebookFormatVersion";
    public const string NotebookKernelLabel = "NotebookKernelLabel";
    public const string NotebookFileNotExist = "NotebookFileNotExist";
    public const string NotebookParseFailed = "NotebookParseFailed";
    public const string NotebookInfoHeader = "NotebookInfoHeader";
    public const string NotebookTotalCells = "NotebookTotalCells";
    public const string NotebookCodeCells = "NotebookCodeCells";
    public const string NotebookMarkdownCells = "NotebookMarkdownCells";
    public const string NotebookCellListHeader = "NotebookCellListHeader";
    public const string NotebookCellContentHeader = "NotebookCellContentHeader";
    public const string NotebookCellSeparator = "NotebookCellSeparator";
    public const string NotebookInvalidCellType = "NotebookInvalidCellType";
    public const string NotebookAddCellFailed = "NotebookAddCellFailed";
    public const string NotebookCellAddedSuccess = "NotebookCellAddedSuccess";
    public const string NotebookDeleteCellFailed = "NotebookDeleteCellFailed";
    public const string NotebookCellDeleted = "NotebookCellDeleted";
    public const string NotebookEditCellFailed = "NotebookEditCellFailed";
    public const string NotebookCellUpdated = "NotebookCellUpdated";
    public const string NotebookMoveCellFailed = "NotebookMoveCellFailed";
    public const string NotebookCellMoved = "NotebookCellMoved";
    public const string NotebookInvalidType = "NotebookInvalidType";
    public const string NotebookChangeCellTypeFailed = "NotebookChangeCellTypeFailed";
    public const string NotebookCellTypeChanged = "NotebookCellTypeChanged";
    public const string NotebookClearOutputsFailed = "NotebookClearOutputsFailed";
    public const string NotebookOutputsCleared = "NotebookOutputsCleared";
    public const string NotebookInvalidCellIndex = "NotebookInvalidCellIndex";
    public const string NotebookCellHeader = "NotebookCellHeader";
    public const string NotebookCellTypeLabel = "NotebookCellTypeLabel";
    public const string NotebookExecutionCountLabel = "NotebookExecutionCountLabel";
    public const string NotebookContentLabel = "NotebookContentLabel";
    public const string NotebookOutputLabel = "NotebookOutputLabel";

    // === NotebookService ===
    public const string NotebookServiceInvalidCellIndex = "NotebookServiceInvalidCellIndex";
    public const string NotebookServiceInvalidSourceIndex = "NotebookServiceInvalidSourceIndex";
    public const string NotebookServiceInvalidTargetIndex = "NotebookServiceInvalidTargetIndex";
    public const string NotebookServiceOnlyCodeCellCanExecute = "NotebookServiceOnlyCodeCellCanExecute";

    // === VoiceService ===
    public const string VoiceStartRecording = "VoiceStartRecording";
    public const string VoiceRecordingDataEmpty = "VoiceRecordingDataEmpty";
    public const string VoiceRecordingComplete = "VoiceRecordingComplete";
    public const string VoiceUnsupportedSttBackend = "VoiceUnsupportedSttBackend";
    public const string VoiceAudioFileNotFound = "VoiceAudioFileNotFound";
    public const string VoiceWhisperApiFailed = "VoiceWhisperApiFailed";
    public const string VoiceWhisperApiCallFailed = "VoiceWhisperApiCallFailed";
    public const string VoiceLocalModelPathInvalid = "VoiceLocalModelPathInvalid";
    public const string VoiceLocalSttNotImplemented = "VoiceLocalSttNotImplemented";
    public const string VoiceRecordLoopError = "VoiceRecordLoopError";

    // === SimpleJsonSchemaValidator ===
    public const string SchemaInvalidJsonInstance = "SchemaInvalidJsonInstance";
    public const string SchemaInvalidJsonSchema = "SchemaInvalidJsonSchema";
    public const string SchemaTypeMismatch = "SchemaTypeMismatch";
    public const string SchemaEnumValueNotAllowed = "SchemaEnumValueNotAllowed";
    public const string SchemaRequiredPropertyMissing = "SchemaRequiredPropertyMissing";
    public const string SchemaAdditionalPropertyNotAllowed = "SchemaAdditionalPropertyNotAllowed";
    public const string SchemaStringTooShort = "SchemaStringTooShort";
    public const string SchemaStringTooLong = "SchemaStringTooLong";
    public const string SchemaNumberTooSmall = "SchemaNumberTooSmall";
    public const string SchemaNumberTooLarge = "SchemaNumberTooLarge";
    public const string SchemaArrayTooFewItems = "SchemaArrayTooFewItems";
    public const string SchemaArrayTooManyItems = "SchemaArrayTooManyItems";

    // === FileEditLogic ===
    public const string FileEditFileNotExist = "FileEditFileNotExist";
    public const string FileEditRegexInvalid = "FileEditRegexInvalid";
    public const string FileEditPatternNotFound = "FileEditPatternNotFound";
    public const string FileEditLineOutOfRange = "FileEditLineOutOfRange";
    public const string FileEditStartLineGreaterThanEnd = "FileEditStartLineGreaterThanEnd";
    public const string FileEditStartLineOutOfRange = "FileEditStartLineOutOfRange";
    public const string FileEditStringNotFound = "FileEditStringNotFound";

    // === SkillService ===
    public const string SkillServiceStartExecution = "SkillServiceStartExecution";
    public const string SkillServiceExecutionComplete = "SkillServiceExecutionComplete";
    public const string SkillServiceExecutionCancelled = "SkillServiceExecutionCancelled";
    public const string SkillServiceExecutionCancelledResult = "SkillServiceExecutionCancelledResult";
    public const string SkillServiceExecutionFailed = "SkillServiceExecutionFailed";
    public const string SkillServiceReloaded = "SkillServiceReloaded";
    public const string SkillServiceReloadAll = "SkillServiceReloadAll";
    public const string SkillServiceReloadFailed = "SkillServiceReloadFailed";
    public const string SkillServiceUnsupportedStepType = "SkillServiceUnsupportedStepType";
    public const string SkillServiceUnknownError = "SkillServiceUnknownError";
    public const string SkillServiceToolExecutionFailed = "SkillServiceToolExecutionFailed";
    public const string SkillServiceMissingRequiredParam = "SkillServiceMissingRequiredParam";

    // === SkillExecutor ===
    public const string SkillExecutorStartExecution = "SkillExecutorStartExecution";
    public const string SkillExecutorExecutionComplete = "SkillExecutorExecutionComplete";
    public const string SkillExecutorExecutionCancelled = "SkillExecutorExecutionCancelled";
    public const string SkillExecutorExecutionCancelledResult = "SkillExecutorExecutionCancelledResult";
    public const string SkillExecutorExecutionFailed = "SkillExecutorExecutionFailed";
    public const string SkillExecutorUnsupportedStepType = "SkillExecutorUnsupportedStepType";
    public const string SkillExecutorToolExecutionFailed = "SkillExecutorToolExecutionFailed";
    public const string SkillExecutorConditionTrue = "SkillExecutorConditionTrue";
    public const string SkillExecutorConditionFalse = "SkillExecutorConditionFalse";
    public const string SkillExecutorMissingRequiredParam = "SkillExecutorMissingRequiredParam";

    // === CodeService ===
    public const string CodeServiceGeneratingCode = "CodeServiceGeneratingCode";
    public const string CodeServiceCachedCodeResult = "CodeServiceCachedCodeResult";
    public const string CodeServiceGenerateCodePrompt = "CodeServiceGenerateCodePrompt";
    public const string CodeServiceGenerateCodeFailed = "CodeServiceGenerateCodeFailed";
    public const string CodeServiceGenerateCancelled = "CodeServiceGenerateCancelled";
    public const string CodeServiceGenerateError = "CodeServiceGenerateError";
    public const string CodeServiceGenerateException = "CodeServiceGenerateException";
    public const string CodeServiceAnalyzingCode = "CodeServiceAnalyzingCode";
    public const string CodeServiceCachedAnalysisResult = "CodeServiceCachedAnalysisResult";
    public const string CodeServiceAnalyzeCodePrompt = "CodeServiceAnalyzeCodePrompt";
    public const string CodeServiceAnalyzeCodeFailed = "CodeServiceAnalyzeCodeFailed";
    public const string CodeServiceAnalyzeCancelled = "CodeServiceAnalyzeCancelled";
    public const string CodeServiceAnalyzeError = "CodeServiceAnalyzeError";
    public const string CodeServiceAnalyzeException = "CodeServiceAnalyzeException";
    public const string CodeServiceExecutingInSandbox = "CodeServiceExecutingInSandbox";
    public const string CodeServiceCodeCannotBeEmpty = "CodeServiceCodeCannotBeEmpty";
    public const string CodeServiceCodeLengthExceeded = "CodeServiceCodeLengthExceeded";
    public const string CodeServiceCodeValidationFailed = "CodeServiceCodeValidationFailed";
    public const string CodeServiceCodeValidationError = "CodeServiceCodeValidationError";
    public const string CodeServiceExecuteCancelled = "CodeServiceExecuteCancelled";
    public const string CodeServiceExecuteFailed = "CodeServiceExecuteFailed";
    public const string CodeServiceExecuteException = "CodeServiceExecuteException";
    public const string CodeServiceExecutionResult = "CodeServiceExecutionResult";

    // === SkillDiscoveryService ===
    public const string SkillDiscoveryCreateDir = "SkillDiscoveryCreateDir";
    public const string SkillDiscoveryFoundCount = "SkillDiscoveryFoundCount";
    public const string SkillDiscoveryFileNotExist = "SkillDiscoveryFileNotExist";
    public const string SkillDiscoveryCannotReadFile = "SkillDiscoveryCannotReadFile";
    public const string SkillDiscoveryJsonNull = "SkillDiscoveryJsonNull";
    public const string SkillDiscoveryJsonParseError = "SkillDiscoveryJsonParseError";
    public const string SkillDiscoveryUnsupportedExtension = "SkillDiscoveryUnsupportedExtension";
    public const string SkillDiscoveryNameEmpty = "SkillDiscoveryNameEmpty";
    public const string SkillDiscoveryDescriptionEmpty = "SkillDiscoveryDescriptionEmpty";
    public const string SkillDiscoveryNoSteps = "SkillDiscoveryNoSteps";
    public const string SkillDiscoveryStepMissingId = "SkillDiscoveryStepMissingId";
    public const string SkillDiscoveryStepIdDuplicate = "SkillDiscoveryStepIdDuplicate";
    public const string SkillDiscoveryStepMissingType = "SkillDiscoveryStepMissingType";
    public const string SkillDiscoveryParamMissingType = "SkillDiscoveryParamMissingType";

    // === ShellBackgroundTaskService ===
    public const string ShellBgCommandCannotBeEmpty = "ShellBgCommandCannotBeEmpty";
    public const string ShellBgTaskCreated = "ShellBgTaskCreated";
    public const string ShellBgTaskCancelled = "ShellBgTaskCancelled";
    public const string ShellBgTaskNotExist = "ShellBgTaskNotExist";
    public const string ShellBgStdoutLabel = "ShellBgStdoutLabel";
    public const string ShellBgStderrLabel = "ShellBgStderrLabel";
    public const string ShellBgExecutionFailed = "ShellBgExecutionFailed";
    public const string ShellBgStartExecution = "ShellBgStartExecution";
    public const string ShellBgTaskCancelledByException = "ShellBgTaskCancelledByException";
    public const string ShellBgTaskExecutionFailed = "ShellBgTaskExecutionFailed";
    public const string ShellBgCancelAgentTasks = "ShellBgCancelAgentTasks";

    // === AgentToolHandlers ===
    public const string AgentCreateFailed = "AgentCreateFailed";
    public const string AgentListFailed = "AgentListFailed";
    public const string AgentSendMessageFailed = "AgentSendMessageFailed";
    public const string AgentGetMessagesFailed = "AgentGetMessagesFailed";
    public const string AgentCoordinatorNotInitialized = "AgentCoordinatorNotInitialized";
    public const string AgentRunningCount = "AgentRunningCount";
    public const string AgentNoRunningAgents = "AgentNoRunningAgents";

    // === WebService ===
    public const string WebInvalidUrl = "WebInvalidUrl";
    public const string WebRedirectLimitExceeded = "WebRedirectLimitExceeded";
    public const string WebRedirectMissingLocation = "WebRedirectMissingLocation";

    // === BriefLogic ===
    public const string BriefFilePathEmpty = "BriefFilePathEmpty";
    public const string BriefFileNotExist = "BriefFileNotExist";
    public const string BriefFileSizeExceeded = "BriefFileSizeExceeded";
    public const string BriefProactiveLabel = "BriefProactiveLabel";
    public const string BriefAttachmentLabel = "BriefAttachmentLabel";

    // === SnipLogic ===
    public const string SnipFileNotFound = "SnipFileNotFound";

    // === PreventSleepService ===
    public const string PreventSleepAlreadyActive = "PreventSleepAlreadyActive";
    public const string PreventSleepSetStateFailed = "PreventSleepSetStateFailed";
    public const string PreventSleepActivated = "PreventSleepActivated";
    public const string PreventSleepRestoreFailed = "PreventSleepRestoreFailed";
    public const string PreventSleepDeactivated = "PreventSleepDeactivated";

    // === VariableResolver ===
    public const string VariableNotExist = "VariableNotExist";

    // === CodeSandboxService ===
    public const string SandboxOutputLabel = "SandboxOutputLabel";
    public const string SandboxErrorLabel = "SandboxErrorLabel";
    public const string SandboxExitCodeLabel = "SandboxExitCodeLabel";

    // === PluginSkillBridge ===
    public const string PluginSkillAlreadyRegistered = "PluginSkillAlreadyRegistered";
    public const string PluginSkillPluginNotLoaded = "PluginSkillPluginNotLoaded";
    public const string PluginSkillRegisterFailed = "PluginSkillRegisterFailed";
    public const string PluginSkillUnregisterFailed = "PluginSkillUnregisterFailed";
    public const string PluginSkillDisposeError = "PluginSkillDisposeError";
    public const string PluginSkillNoRegisteredSkills = "PluginSkillNoRegisteredSkills";
    public const string PluginSkillActionParam = "PluginSkillActionParam";
    public const string PluginSkillInputParam = "PluginSkillInputParam";

    // === McpSkillAdapter ===
    public const string McpAdapterCallToolDescription = "McpAdapterCallToolDescription";
    public const string McpAdapterFormatResultDescription = "McpAdapterFormatResultDescription";
    public const string McpAdapterFormatResultPrompt = "McpAdapterFormatResultPrompt";
    public const string McpAdapterAdaptFailed = "McpAdapterAdaptFailed";
    public const string McpAdapterToolExecutionFailed = "McpAdapterToolExecutionFailed";
    public const string McpAdapterExecuteFailed = "McpAdapterExecuteFailed";
    public const string McpAdapterBuildToolPrompt = "McpAdapterBuildToolPrompt";

    // === McpSkillProvider ===
    public const string McpProviderSkillNotExist = "McpProviderSkillNotExist";
    public const string McpProviderAdapterNotFound = "McpProviderAdapterNotFound";
    public const string McpProviderRefreshFailed = "McpProviderRefreshFailed";
    public const string McpProviderDisposeFailed = "McpProviderDisposeFailed";

    // === API Exceptions ===
    public const string ApiAuthFailed = "ApiAuthFailed";
    public const string ApiRateLimited = "ApiRateLimited";
    public const string ApiRateLimitedRetryAfter = "ApiRateLimitedRetryAfter";
    public const string ApiRateLimitedRetryLater = "ApiRateLimitedRetryLater";
    public const string ApiServerError = "ApiServerError";
    public const string ApiValidationFailed = "ApiValidationFailed";

    // === RetryPolicy ===
    public const string RetryExhausted = "RetryExhausted";

    // === ApiClient ===
    public const string ApiClientRequestFailed = "ApiClientRequestFailed";
    public const string ApiClientRequestRetry = "ApiClientRequestRetry";
    public const string ApiClientJsonDeserializationFailed = "ApiClientJsonDeserializationFailed";
    public const string ApiClientRequestFailedGeneric = "ApiClientRequestFailedGeneric";
    public const string ApiClientDefaultAuthFailed = "ApiClientDefaultAuthFailed";
    public const string ApiClientDefaultInvalidParams = "ApiClientDefaultInvalidParams";

    // === UsageTracker ===
    public const string UsageTrackerRecord = "UsageTrackerRecord";
    public const string UsageTrackerExtractFailed = "UsageTrackerExtractFailed";

    // === ApiLoggingHandler ===
    public const string ApiLoggingRequestLogFailed = "ApiLoggingRequestLogFailed";
    public const string ApiLoggingResponseNull = "ApiLoggingResponseNull";
    public const string ApiLoggingResponseLogFailed = "ApiLoggingResponseLogFailed";

    // === MemoryCacheService ===
    public const string CacheHit = "CacheHit";
    public const string CacheMiss = "CacheMiss";
    public const string CacheSet = "CacheSet";
    public const string CacheDefault30Min = "CacheDefault30Min";
    public const string CacheRemoved = "CacheRemoved";
    public const string CacheCleared = "CacheCleared";

    // === VcrService ===
    public const string VcrNotRecordMode = "VcrNotRecordMode";
    public const string VcrNotPlaybackMode = "VcrNotPlaybackMode";
    public const string VcrNoMatchingInteraction = "VcrNoMatchingInteraction";
    public const string VcrNoMatchingInteractionStrict = "VcrNoMatchingInteractionStrict";

    // === SkillSearchService ===
    public const string SkillSearchIndexRebuilt = "SkillSearchIndexRebuilt";
}
