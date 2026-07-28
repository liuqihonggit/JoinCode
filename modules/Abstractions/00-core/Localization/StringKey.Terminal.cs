namespace JoinCode.Abstractions.Localization;

public static partial class StringKey
{
    // === Status Bar / Footer (Host UI) ===
    public const string FooterExitHint = "FooterExitHint";
    public const string FooterInterruptHint = "FooterInterruptHint";
    public const string FooterBashModeHint = "FooterBashModeHint";
    public const string FooterShortcutsHint = "FooterShortcutsHint";
    public const string PermissionModeDefault = "PermissionModeDefault";
    public const string PermissionModePlan = "PermissionModePlan";
    public const string PermissionModePlanShort = "PermissionModePlanShort";
    public const string PermissionModeAcceptEdits = "PermissionModeAcceptEdits";
    public const string PermissionModeAcceptEditsShort = "PermissionModeAcceptEditsShort";
    public const string PermissionModeBypassPermissions = "PermissionModeBypassPermissions";
    public const string PermissionModeBypassPermissionsShort = "PermissionModeBypassPermissionsShort";
    public const string PermissionModeDontAsk = "PermissionModeDontAsk";
    public const string PermissionModeDontAskShort = "PermissionModeDontAskShort";
    public const string PermissionModeAuto = "PermissionModeAuto";
    public const string PermissionModeAutoShort = "PermissionModeAutoShort";
    public const string PermissionCycleHint = "PermissionCycleHint";
    public const string NonInteractivePermissionDenied = "NonInteractivePermissionDenied";
    public const string PermissionAllowLabel = "PermissionAllowLabel";
    public const string PermissionYes = "PermissionYes";
    public const string PermissionNo = "PermissionNo";
    public const string PermissionAlways = "PermissionAlways";
    public const string PermissionAnyBashCommandStartingWith = "PermissionAnyBashCommandStartingWith";
    public const string PermissionAnyBashCommand = "PermissionAnyBashCommand";
    public const string PermissionTheBashCommand = "PermissionTheBashCommand";
    public const string PermissionTellWhatToDoNext = "PermissionTellWhatToDoNext";
    public const string PermissionTellWhatToDoDifferently = "PermissionTellWhatToDoDifferently";
    public const string PermissionDoYouWantToProceed = "PermissionDoYouWantToProceed";

    // === Thinking / Expand Hints (MessageRenderer, ExpandHint) ===
    public const string ThinkingExpandHint = "ThinkingExpandHint";
    public const string ThinkingLabel = "ThinkingLabel";
    public const string RedactedThinkingLabel = "RedactedThinkingLabel";
    public const string CompactSummaryTitle = "CompactSummaryTitle";
    public const string ExpandHistoryHint = "ExpandHistoryHint";
    public const string ConversationCompacted = "ConversationCompacted";
    public const string ExpandHintText = "ExpandHintText";
    public const string CompactSummarizedLabel = "CompactSummarizedLabel";
    public const string CompactTokensSaved = "CompactTokensSaved";
    public const string CompactContextLabel = "CompactContextLabel";

    // === VcrToolHandlers ===
    public const string CassetteNameCannotBeEmpty = "CassetteNameCannotBeEmpty";
    public const string VcrRecordStarted = "VcrRecordStarted";
    public const string VcrRecordStartFailed = "VcrRecordStartFailed";
    public const string VcrRecordStartFailedLog = "VcrRecordStartFailedLog";
    public const string VcrPlaybackStarted = "VcrPlaybackStarted";
    public const string VcrPlaybackLabelCassetteName = "VcrPlaybackLabelCassetteName";
    public const string VcrPlaybackStartFailed = "VcrPlaybackStartFailed";
    public const string VcrPlaybackStartFailedLog = "VcrPlaybackStartFailedLog";
    public const string VcrServiceStatus = "VcrServiceStatus";
    public const string VcrLabelCurrentMode = "VcrLabelCurrentMode";

    // === TerminalCaptureToolHandlers ===
    public const string TerminalCaptureFailedLog = "TerminalCaptureFailedLog";
    public const string TerminalCaptureFailed = "TerminalCaptureFailed";
    public const string TerminalBufferCapture = "TerminalBufferCapture";
    public const string BufferCaptureUnavailable = "BufferCaptureUnavailable";
    public const string UseScreenModeCapture = "UseScreenModeCapture";
    public const string TerminalLabelSize = "TerminalLabelSize";
    public const string TerminalLabelCaptureTime = "TerminalLabelCaptureTime";
    public const string TerminalScreenCapture = "TerminalScreenCapture";
    public const string TerminalCapture = "TerminalCapture";
    public const string TerminalLabelTerminalSize = "TerminalLabelTerminalSize";
    public const string TerminalLabelBufferSize = "TerminalLabelBufferSize";
    public const string OutputRedirectedCannotCapture = "OutputRedirectedCannotCapture";
    public const string CaptureServiceNotEnabled = "CaptureServiceNotEnabled";
    public const string TerminalLabelCaptureLimit = "TerminalLabelCaptureLimit";
    public const string PlatformNotSupportTerminalCapture = "PlatformNotSupportTerminalCapture";

    // === SyntheticOutputToolHandlers ===
    public const string OutputFormatCannotBeEmpty = "OutputFormatCannotBeEmpty";
    public const string JsonSchemaInvalid = "JsonSchemaInvalid";
    public const string StructuredOutputInstruction = "StructuredOutputInstruction";
    public const string TerminalLabelOutputFormat = "TerminalLabelOutputFormat";
    public const string TerminalLabelContentHint = "TerminalLabelContentHint";
    public const string PleaseOutputStructuredContent = "PleaseOutputStructuredContent";
    public const string StructuredOutputGenerateFailedLog = "StructuredOutputGenerateFailedLog";
    public const string StructuredOutputGenerateFailed = "StructuredOutputGenerateFailed";
    public const string InvalidJson = "InvalidJson";
    public const string InvalidSchema = "InvalidSchema";
    public const string StructuredOutputValidationFailed = "StructuredOutputValidationFailed";
    public const string StructuredOutputValidation = "StructuredOutputValidation";
    public const string ValidationResultPassed = "ValidationResultPassed";
    public const string TerminalLabelFormattedOutput = "TerminalLabelFormattedOutput";
    public const string TerminalLabelRawOutput = "TerminalLabelRawOutput";

    // === ReplToolHandlers ===
    public const string ReplServiceNotEnabled = "ReplServiceNotEnabled";
    public const string ReplModeEnabled = "ReplModeEnabled";
    public const string ReplModeDisabled = "ReplModeDisabled";
    public const string ReplLabelMode = "ReplLabelMode";
    public const string ReplEnabled = "ReplEnabled";
    public const string ReplDisabled = "ReplDisabled";
    public const string ReplLabelHiddenTools = "ReplLabelHiddenTools";
    public const string ReplLabelLanguage = "ReplLabelLanguage";
    public const string ProvideCodeOrManageMode = "ProvideCodeOrManageMode";
    public const string ReplResultLabel = "ReplResultLabel";
    public const string ReplSuccess = "ReplSuccess";
    public const string ReplFailed = "ReplFailed";
    public const string TerminalLabelExecutionTime = "TerminalLabelExecutionTime";
    public const string TerminalLabelOutput = "TerminalLabelOutput";
    public const string TerminalLabelError = "TerminalLabelError";
    public const string ReplExecutionFailedLog = "ReplExecutionFailedLog";
    public const string ReplExecutionFailed = "ReplExecutionFailed";
}
