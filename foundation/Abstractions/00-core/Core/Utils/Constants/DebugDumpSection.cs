namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 调试信息 dump 选项位标志枚举 — 启动时用户交互选择要显示的诊断信息类别
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 DebugDumpSectionConstants + DebugDumpSectionExtensions
/// 决策: 位标志而非 bool，支持用户选择组合（如 Init+Prompt = 1+16 = 17）
/// 交互解析支持: 字母(i/e/w/l/p/a)、单词(init/error/warn/log/prompt/all/none)、数字(0/1/2/4/8/16/31)
/// </summary>
[Flags]
public enum DebugDumpSection
{
    /// <summary>不打开任何调试信息</summary>
    [EnumValue("0")]
    [EnumValue("n")]
    [EnumValue("none")]
    None = 0,

    /// <summary>初始化状态 — 调试日志状态/环境变量/崩溃快照/日志缓冲区/MCP工具/系统提示词部分清单</summary>
    [EnumValue("1")]
    [EnumValue("i")]
    [EnumValue("init")]
    Init = 1,

    /// <summary>错误 — CrashSnapshotStore 中的 Error/Fatal + 诊断错误日志</summary>
    [EnumValue("2")]
    [EnumValue("e")]
    [EnumValue("error")]
    Error = 2,

    /// <summary>警告和错误 — CrashSnapshotStore 中的 Warning + Error/Fatal + 诊断错误日志</summary>
    [EnumValue("4")]
    [EnumValue("w")]
    [EnumValue("warn")]
    Warn = 4,

    /// <summary>诊断日志 — DebugLogBuffer 中的最近日志条目</summary>
    [EnumValue("8")]
    [EnumValue("l")]
    [EnumValue("log")]
    Log = 8,

    /// <summary>系统提示词 — ISystemPromptProvider.GetSections() 的所有部分内容</summary>
    [EnumValue("16")]
    [EnumValue("p")]
    [EnumValue("prompt")]
    Prompt = 16,

    /// <summary>全部 — Init | Error | Warn | Log | Prompt = 31</summary>
    [EnumValue("a")]
    [EnumValue("all")]
    [EnumValue("31")]
    All = Init | Error | Warn | Log | Prompt,
}
