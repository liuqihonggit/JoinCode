namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Brief/System/其他小工具名称枚举
/// </summary>
public enum SystemToolName
{
    [EnumValue("brief_mode")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    BriefMode,

    [EnumValue("brief_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    BriefStatus,

    [EnumValue("Brief")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Brief,

    [EnumValue("SendUserMessage")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SendUserMessage,

    [EnumValue("Sleep")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    Sleep,

    [EnumValue("sleep_until")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SleepUntil,

    [EnumValue("TaskOutput")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskOutput,

    [EnumValue("ToolSearch")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ToolSearch,

    [EnumValue("verify_plan_execution")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    VerifyPlanExecution,

    [EnumValue("ctx_inspect")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CtxInspect,

    [EnumValue("terminal_capture")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TerminalCapture,

    [EnumValue("snip")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Snip,

    [EnumValue("StructuredOutput")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SyntheticOutput,

    [EnumValue("RemoteTrigger")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    RemoteTrigger,

    [EnumValue("monitor")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    Monitor,

    [EnumValue("send_user_file")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SendUserFile,

    [EnumValue("push_notification")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    PushNotification,

    [EnumValue("voice_start_recording")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VoiceStartRecording,

    [EnumValue("voice_stop_recording")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VoiceStopRecording,

    [EnumValue("voice_transcribe")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VoiceTranscribe,

    [EnumValue("voice_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    VoiceStatus,

    [EnumValue("vcr_record")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VcrRecord,

    [EnumValue("vcr_playback")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VcrPlayback,

    [EnumValue("vcr_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    VcrStatus,

    [EnumValue("subscribe_pr")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SubscribePR,

    [EnumValue("list_peers")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ListPeers,

    [EnumValue("REPL")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    Repl,

    [EnumValue("structured_output_register")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    StructuredOutputRegister,

    [EnumValue("structured_output_validate")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    StructuredOutputValidate,

    [EnumValue("goal_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GoalGet,

    [EnumValue("goal_update")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GoalUpdate,

    [EnumValue("goal_graph_define")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GoalGraphDefine,

    [EnumValue("resume_timed_out_task")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ResumeTimedOutTask,

    [EnumValue("build_output")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    BuildOutput,

    /// <summary>
    /// 模型查找工具 — 按功能→型号渐进式展开模型表，模态不匹配报错时动态暴露。
    /// 语法对齐 ToolSearch：list_groups / map[功能Key] / map[功能Key][vendor] / 关键词
    /// </summary>
    [EnumValue("ModelSearch")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ModelSearch,
}
