namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Brief/System/其他小工具名称枚举
/// </summary>
public enum SystemToolName
{
    [EnumValue("brief_mode")] BriefMode,
    [EnumValue("brief_status")] BriefStatus,
    [EnumValue("Brief")] Brief,
    [EnumValue("SendUserMessage")] SendUserMessage,
    [EnumValue("Sleep")] Sleep,
    [EnumValue("sleep_until")] SleepUntil,
    [EnumValue("TaskOutput")] TaskOutput,
    [EnumValue("ToolSearch")] ToolSearch,
    [EnumValue("verify_plan_execution")] VerifyPlanExecution,
    [EnumValue("ctx_inspect")] CtxInspect,
    [EnumValue("terminal_capture")] TerminalCapture,
    [EnumValue("snip")] Snip,
    [EnumValue("StructuredOutput")] SyntheticOutput,
    [EnumValue("RemoteTrigger")] RemoteTrigger,
    [EnumValue("monitor")] Monitor,
    [EnumValue("send_user_file")] SendUserFile,
    [EnumValue("push_notification")] PushNotification,
    [EnumValue("voice_start_recording")] VoiceStartRecording,
    [EnumValue("voice_stop_recording")] VoiceStopRecording,
    [EnumValue("voice_transcribe")] VoiceTranscribe,
    [EnumValue("voice_status")] VoiceStatus,
    [EnumValue("vcr_record")] VcrRecord,
    [EnumValue("vcr_playback")] VcrPlayback,
    [EnumValue("vcr_status")] VcrStatus,
    [EnumValue("subscribe_pr")] SubscribePR,
    [EnumValue("list_peers")] ListPeers,
    [EnumValue("REPL")] Repl,
    [EnumValue("structured_output_register")] StructuredOutputRegister,
    [EnumValue("structured_output_validate")] StructuredOutputValidate,
    [EnumValue("goal_get")] GoalGet,
    [EnumValue("goal_update")] GoalUpdate,
}
