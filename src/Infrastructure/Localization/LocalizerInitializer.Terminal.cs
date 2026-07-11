namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterTerminalEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === Status Bar / Footer (Host UI) ===
        defaultEntries[StringKey.FooterExitHint] = "Esc to exit";
        defaultEntries[StringKey.FooterInterruptHint] = "esc to interrupt";
        defaultEntries[StringKey.FooterBashModeHint] = "! bash mode";
        defaultEntries[StringKey.FooterShortcutsHint] = "? for shortcuts";
        defaultEntries[StringKey.PermissionModeDefault] = "default";
        defaultEntries[StringKey.PermissionModePlan] = "plan";
        defaultEntries[StringKey.PermissionModePlanShort] = "plan";
        defaultEntries[StringKey.PermissionModeAcceptEdits] = "accept edits";
        defaultEntries[StringKey.PermissionModeAcceptEditsShort] = "accept";
        defaultEntries[StringKey.PermissionModeBypassPermissions] = "bypass permissions";
        defaultEntries[StringKey.PermissionModeBypassPermissionsShort] = "bypass";
        defaultEntries[StringKey.PermissionModeDontAsk] = "don't ask";
        defaultEntries[StringKey.PermissionModeDontAskShort] = "dont-ask";
        defaultEntries[StringKey.PermissionModeAuto] = "auto";
        defaultEntries[StringKey.PermissionModeAutoShort] = "auto";
        defaultEntries[StringKey.PermissionCycleHint] = "Tab to cycle";
        defaultEntries[StringKey.NonInteractivePermissionDenied] = "Non-interactive mode: permission denied by default.";
        defaultEntries[StringKey.PermissionAllowLabel] = "Allow?";
        defaultEntries[StringKey.PermissionYes] = "Yes";
        defaultEntries[StringKey.PermissionNo] = "No";
        defaultEntries[StringKey.PermissionAlways] = "Always";
        defaultEntries[StringKey.PermissionAnyBashCommandStartingWith] = "Any bash command starting with {0}";
        defaultEntries[StringKey.PermissionAnyBashCommand] = "Any bash command";
        defaultEntries[StringKey.PermissionTheBashCommand] = "The bash command: {0}";
        defaultEntries[StringKey.PermissionTellWhatToDoNext] = "tell Claude what to do next";
        defaultEntries[StringKey.PermissionTellWhatToDoDifferently] = "tell Claude what to do differently";
        defaultEntries[StringKey.PermissionDoYouWantToProceed] = "Do you want to proceed?";

        zhEntries[StringKey.FooterExitHint] = "Esc 退出";
        zhEntries[StringKey.FooterInterruptHint] = "esc 中断";
        zhEntries[StringKey.FooterBashModeHint] = "! bash 模式";
        // Claude Code uses English UI — zhEntries mirror defaultEntries for consistency
        zhEntries[StringKey.FooterShortcutsHint] = "? for shortcuts";
        zhEntries[StringKey.PermissionModeDefault] = "默认";
        zhEntries[StringKey.PermissionModePlan] = "计划模式";
        zhEntries[StringKey.PermissionModePlanShort] = "计划";
        zhEntries[StringKey.PermissionModeAcceptEdits] = "接受编辑";
        zhEntries[StringKey.PermissionModeAcceptEditsShort] = "接受";
        zhEntries[StringKey.PermissionModeBypassPermissions] = "绕过权限";
        zhEntries[StringKey.PermissionModeBypassPermissionsShort] = "绕过";
        zhEntries[StringKey.PermissionModeDontAsk] = "不再询问";
        zhEntries[StringKey.PermissionModeDontAskShort] = "不问";
        zhEntries[StringKey.PermissionModeAuto] = "自动";
        zhEntries[StringKey.PermissionModeAutoShort] = "自动";
        zhEntries[StringKey.PermissionCycleHint] = "Tab 切换";
        zhEntries[StringKey.NonInteractivePermissionDenied] = "非交互模式：默认拒绝权限。";
        zhEntries[StringKey.PermissionAllowLabel] = "允许?";
        zhEntries[StringKey.PermissionYes] = "是";
        zhEntries[StringKey.PermissionNo] = "否";
        zhEntries[StringKey.PermissionAlways] = "始终";
        zhEntries[StringKey.PermissionAnyBashCommandStartingWith] = "任何以 {0} 开头的 bash 命令";
        zhEntries[StringKey.PermissionAnyBashCommand] = "任何 bash 命令";
        zhEntries[StringKey.PermissionTheBashCommand] = "bash 命令: {0}";
        zhEntries[StringKey.PermissionTellWhatToDoNext] = "告诉 Claude 接下来做什么";
        zhEntries[StringKey.PermissionTellWhatToDoDifferently] = "告诉 Claude 做什么不同的";
        zhEntries[StringKey.PermissionDoYouWantToProceed] = "是否继续？";

        // === Thinking / Expand Hints (MessageRenderer, ExpandHint) ===
        defaultEntries[StringKey.ThinkingExpandHint] = "ctrl+o to expand";
        defaultEntries[StringKey.ThinkingLabel] = "Thinking";
        defaultEntries[StringKey.RedactedThinkingLabel] = "Thinking (redacted)";
        defaultEntries[StringKey.CompactSummaryTitle] = "Compact summary";
        defaultEntries[StringKey.ExpandHistoryHint] = "ctrl+o to expand history";
        defaultEntries[StringKey.ExpandHintText] = "ctrl+o to expand";
        defaultEntries[StringKey.CompactSummarizedLabel] = "Summarized {0} messages {1}";
        defaultEntries[StringKey.CompactTokensSaved] = "{0} tokens saved";
        defaultEntries[StringKey.CompactContextLabel] = "Context: {0}";
        defaultEntries[StringKey.CompactDirectionUpTo] = "up to";
        defaultEntries[StringKey.CompactDirectionFromThisPoint] = "from this point";

        zhEntries[StringKey.ThinkingExpandHint] = "ctrl+o 展开";
        zhEntries[StringKey.ThinkingLabel] = "思考中";
        zhEntries[StringKey.RedactedThinkingLabel] = "思考中";
        zhEntries[StringKey.CompactSummaryTitle] = "对话摘要";
        zhEntries[StringKey.ExpandHistoryHint] = "ctrl+o 展开历史";
        zhEntries[StringKey.ExpandHintText] = "ctrl+o 展开";
        zhEntries[StringKey.CompactSummarizedLabel] = "已摘要 {0} 条消息{1}";
        zhEntries[StringKey.CompactTokensSaved] = "{0} tokens 已节省";
        zhEntries[StringKey.CompactContextLabel] = "上下文: {0}";
        zhEntries[StringKey.CompactDirectionUpTo] = "直至该消息";
        zhEntries[StringKey.CompactDirectionFromThisPoint] = "从此消息起";

        // === VcrToolHandlers ===
        defaultEntries[StringKey.CassetteNameCannotBeEmpty] = "Cassette name cannot be empty";
        defaultEntries[StringKey.VcrRecordStarted] = "VCR recording started, cassette: {0}";
        defaultEntries[StringKey.VcrRecordStartFailed] = "VCR recording start failed: {0}";
        defaultEntries[StringKey.VcrRecordStartFailedLog] = "VCR recording start failed: {CassetteName}";
        defaultEntries[StringKey.VcrPlaybackStarted] = "VCR playback started, cassette: {0}";
        defaultEntries[StringKey.VcrPlaybackLabelCassetteName] = "Cassette name: {0}";
        defaultEntries[StringKey.VcrPlaybackStartFailed] = "VCR playback start failed: {0}";
        defaultEntries[StringKey.VcrPlaybackStartFailedLog] = "VCR playback start failed: {CassetteName}";
        defaultEntries[StringKey.VcrServiceStatus] = "VCR service status";
        defaultEntries[StringKey.VcrLabelCurrentMode] = "Current mode: {0}";

        zhEntries[StringKey.CassetteNameCannotBeEmpty] = "磁带名称不能为空";
        zhEntries[StringKey.VcrRecordStarted] = "VCR录制已开始，磁带: {0}";
        zhEntries[StringKey.VcrRecordStartFailed] = "VCR录制启动失败: {0}";
        zhEntries[StringKey.VcrRecordStartFailedLog] = "VCR录制启动失败: {CassetteName}";
        zhEntries[StringKey.VcrPlaybackStarted] = "VCR回放已开始，磁带: {0}";
        zhEntries[StringKey.VcrPlaybackLabelCassetteName] = "磁带名称: {0}";
        zhEntries[StringKey.VcrPlaybackStartFailed] = "VCR回放启动失败: {0}";
        zhEntries[StringKey.VcrPlaybackStartFailedLog] = "VCR回放启动失败: {CassetteName}";
        zhEntries[StringKey.VcrServiceStatus] = "VCR服务状态";
        zhEntries[StringKey.VcrLabelCurrentMode] = "当前模式: {0}";

        // === TerminalCaptureToolHandlers ===
        defaultEntries[StringKey.TerminalCaptureFailedLog] = "Terminal capture failed";
        defaultEntries[StringKey.TerminalCaptureFailed] = "Terminal capture failed: {0}";
        defaultEntries[StringKey.TerminalBufferCapture] = "Terminal buffer capture";
        defaultEntries[StringKey.BufferCaptureUnavailable] = "Buffer capture unavailable (terminal output may be redirected)";
        defaultEntries[StringKey.UseScreenModeCapture] = "Please use screen mode to capture the current visible area";
        defaultEntries[StringKey.TerminalLabelSize] = "Size: {0}x{1}";
        defaultEntries[StringKey.TerminalLabelCaptureTime] = "Captured at: {0}";
        defaultEntries[StringKey.TerminalScreenCapture] = "Terminal screen capture";
        defaultEntries[StringKey.TerminalCapture] = "Terminal capture";
        defaultEntries[StringKey.TerminalLabelTerminalSize] = "Terminal size: {0}x{1}";
        defaultEntries[StringKey.TerminalLabelBufferSize] = "Buffer: {0}x{1}";
        defaultEntries[StringKey.OutputRedirectedCannotCapture] = "(Terminal output redirected, cannot capture screen content)";
        defaultEntries[StringKey.CaptureServiceNotEnabled] = "Terminal capture service not enabled, showing metadata only";
        defaultEntries[StringKey.TerminalLabelCaptureLimit] = "(Capture limit: {0} lines)";
        defaultEntries[StringKey.PlatformNotSupportTerminalCapture] = "Current platform does not support terminal capture";

        zhEntries[StringKey.TerminalCaptureFailedLog] = "终端捕获失败";
        zhEntries[StringKey.TerminalCaptureFailed] = "终端捕获失败: {0}";
        zhEntries[StringKey.TerminalBufferCapture] = "终端缓冲区捕获";
        zhEntries[StringKey.BufferCaptureUnavailable] = "缓冲区捕获不可用（终端输出可能已重定向）";
        zhEntries[StringKey.UseScreenModeCapture] = "请使用 screen 模式捕获当前可见区域";
        zhEntries[StringKey.TerminalLabelSize] = "尺寸: {0}x{1}";
        zhEntries[StringKey.TerminalLabelCaptureTime] = "捕获时间: {0}";
        zhEntries[StringKey.TerminalScreenCapture] = "终端屏幕捕获";
        zhEntries[StringKey.TerminalCapture] = "终端捕获";
        zhEntries[StringKey.TerminalLabelTerminalSize] = "终端大小: {0}x{1}";
        zhEntries[StringKey.TerminalLabelBufferSize] = "缓冲区: {0}x{1}";
        zhEntries[StringKey.OutputRedirectedCannotCapture] = "(终端输出已重定向，无法捕获屏幕内容)";
        zhEntries[StringKey.CaptureServiceNotEnabled] = "终端捕获服务未启用，仅显示元数据";
        zhEntries[StringKey.TerminalLabelCaptureLimit] = "(捕获限制: {0} 行)";
        zhEntries[StringKey.PlatformNotSupportTerminalCapture] = "当前平台不支持终端捕获";

        // === SyntheticOutputToolHandlers ===
        defaultEntries[StringKey.OutputFormatCannotBeEmpty] = "output_format cannot be empty";
        defaultEntries[StringKey.JsonSchemaInvalid] = "Invalid JSON Schema: {0}";
        defaultEntries[StringKey.StructuredOutputInstruction] = "Structured output instruction";
        defaultEntries[StringKey.TerminalLabelOutputFormat] = "Output format: {0}";
        defaultEntries[StringKey.TerminalLabelContentHint] = "Content hint: {0}";
        defaultEntries[StringKey.PleaseOutputStructuredContent] = "Please output structured content in the specified format in subsequent responses.";
        defaultEntries[StringKey.StructuredOutputGenerateFailedLog] = "Structured output generation failed";
        defaultEntries[StringKey.StructuredOutputGenerateFailed] = "Structured output generation failed: {0}";
        defaultEntries[StringKey.InvalidJson] = "Invalid JSON: {0}";
        defaultEntries[StringKey.InvalidSchema] = "Invalid Schema: {0}";
        defaultEntries[StringKey.StructuredOutputValidationFailed] = "Structured output validation failed:\n{0}";
        defaultEntries[StringKey.StructuredOutputValidation] = "Structured output validation - {0}";
        defaultEntries[StringKey.ValidationResultPassed] = "Validation result: passed";
        defaultEntries[StringKey.TerminalLabelFormattedOutput] = "[Formatted output]";
        defaultEntries[StringKey.TerminalLabelRawOutput] = "[Raw output]";

        zhEntries[StringKey.OutputFormatCannotBeEmpty] = "output_format 不能为空";
        zhEntries[StringKey.JsonSchemaInvalid] = "JSON Schema 无效: {0}";
        zhEntries[StringKey.StructuredOutputInstruction] = "结构化输出指令";
        zhEntries[StringKey.TerminalLabelOutputFormat] = "输出格式: {0}";
        zhEntries[StringKey.TerminalLabelContentHint] = "内容提示: {0}";
        zhEntries[StringKey.PleaseOutputStructuredContent] = "请在后续回复中按照上述格式输出结构化内容。";
        zhEntries[StringKey.StructuredOutputGenerateFailedLog] = "结构化输出生成失败";
        zhEntries[StringKey.StructuredOutputGenerateFailed] = "结构化输出生成失败: {0}";
        zhEntries[StringKey.InvalidJson] = "无效的JSON: {0}";
        zhEntries[StringKey.InvalidSchema] = "无效的Schema: {0}";
        zhEntries[StringKey.StructuredOutputValidationFailed] = "结构化输出验证失败:\n{0}";
        zhEntries[StringKey.StructuredOutputValidation] = "结构化输出验证 - {0}";
        zhEntries[StringKey.ValidationResultPassed] = "验证结果: 通过";
        zhEntries[StringKey.TerminalLabelFormattedOutput] = "[格式化输出]";
        zhEntries[StringKey.TerminalLabelRawOutput] = "[原始输出]";

        // === ReplToolHandlers ===
        defaultEntries[StringKey.ReplServiceNotEnabled] = "REPL service not enabled";
        defaultEntries[StringKey.ReplModeEnabled] = "REPL mode enabled. Basic tools (file_read/write/edit, glob, grep, bash, notebook_edit, agent) will be hidden, accessible only via REPL.";
        defaultEntries[StringKey.ReplModeDisabled] = "REPL mode disabled. All tools restored.";
        defaultEntries[StringKey.ReplLabelMode] = "REPL mode: {0}";
        defaultEntries[StringKey.ReplEnabled] = "enabled";
        defaultEntries[StringKey.ReplDisabled] = "disabled";
        defaultEntries[StringKey.ReplLabelHiddenTools] = "Hidden tools:";
        defaultEntries[StringKey.ReplLabelLanguage] = "REPL ({0})";
        defaultEntries[StringKey.ProvideCodeOrManageMode] = "Provide the code parameter to execute code, or use action=enable/disable/status to manage mode";
        defaultEntries[StringKey.ReplResultLabel] = "REPL ({0}) - {1}";
        defaultEntries[StringKey.ReplSuccess] = "success";
        defaultEntries[StringKey.ReplFailed] = "failed";
        defaultEntries[StringKey.TerminalLabelExecutionTime] = "Execution time: {0}ms";
        defaultEntries[StringKey.TerminalLabelOutput] = "[Output]";
        defaultEntries[StringKey.TerminalLabelError] = "[Error]";
        defaultEntries[StringKey.ReplExecutionFailedLog] = "REPL execution failed";
        defaultEntries[StringKey.ReplExecutionFailed] = "REPL execution failed: {0}";

        zhEntries[StringKey.ReplServiceNotEnabled] = "REPL 服务未启用";
        zhEntries[StringKey.ReplModeEnabled] = "REPL 模式已启用。基础工具（file_read/write/edit, glob, grep, bash, notebook_edit, agent）将被隐藏，仅通过 REPL 访问。";
        zhEntries[StringKey.ReplModeDisabled] = "REPL 模式已禁用。所有工具恢复可用。";
        zhEntries[StringKey.ReplLabelMode] = "REPL 模式: {0}";
        zhEntries[StringKey.ReplEnabled] = "已启用";
        zhEntries[StringKey.ReplDisabled] = "已禁用";
        zhEntries[StringKey.ReplLabelHiddenTools] = "隐藏的工具:";
        zhEntries[StringKey.ReplLabelLanguage] = "REPL ({0})";
        zhEntries[StringKey.ProvideCodeOrManageMode] = "请提供 code 参数执行代码，或使用 action=enable/disable/status 管理模式";
        zhEntries[StringKey.ReplResultLabel] = "REPL ({0}) - {1}";
        zhEntries[StringKey.ReplSuccess] = "成功";
        zhEntries[StringKey.ReplFailed] = "失败";
        zhEntries[StringKey.TerminalLabelExecutionTime] = "执行时间: {0}ms";
        zhEntries[StringKey.TerminalLabelOutput] = "[输出]";
        zhEntries[StringKey.TerminalLabelError] = "[错误]";
        zhEntries[StringKey.ReplExecutionFailedLog] = "REPL 执行失败";
        zhEntries[StringKey.ReplExecutionFailed] = "REPL 执行失败: {0}";
    }
}
