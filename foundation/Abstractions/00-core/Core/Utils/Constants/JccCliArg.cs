namespace JoinCode.Abstractions.Utils;

/// <summary>
/// CLI 参数名枚举 — [EnumValue] 由 EnumMetadataGenerator 自动生成 JccCliArgConstants + JccCliArgExtensions
/// 第一个 [EnumValue] 为主值（长名），后续为别名（短名），FromValue 均可匹配
/// 所有 CLI 参数名字符串的唯一数据源，CliArg/BridgeCliArg/DreamCliArg 的 [CliOption] 引用此常量
/// </summary>
public enum JccCliArg
{
    [EnumValue("--help")]
    [EnumValue("-h")]
    Help,

    [EnumValue("--version")]
    [EnumValue("-v")]
    Version,

    [EnumValue("--pipe")]
    Pipe,

    [EnumValue("--prompt")]
    [EnumValue("-p")]
    Prompt,

    [EnumValue("--model")]
    [EnumValue("-m")]
    Model,

    [EnumValue("--vendor")]
    Vendor,

    [EnumValue("--non-interactive")]
    NonInteractive,

    [EnumValue("--no-confirm")]
    NoConfirm,

    [EnumValue("--trust")]
    Trust,

    [EnumValue("--brief")]
    Brief,

    [EnumValue("--force-interactive")]
    ForceInteractive,

    [EnumValue("--tui")]
    Tui,

    [EnumValue("--await")]
    Await,

    [EnumValue("--debuglog")]
    [EnumValue("-d")]
    DebugLog,

    [EnumValue("--continue")]
    [EnumValue("-c")]
    Continue,

    [EnumValue("--resume")]
    [EnumValue("-r")]
    Resume,

    [EnumValue("--permission-mode")]
    PermissionMode,

    [EnumValue("--dangerously-skip-permissions")]
    DangerouslySkipPermissions,

    [EnumValue("--allowed-tools")]
    AllowedTools,

    [EnumValue("--disallowed-tools")]
    DisallowedTools,

    [EnumValue("--system-prompt")]
    SystemPrompt,

    [EnumValue("--append-system-prompt")]
    AppendSystemPrompt,

    [EnumValue("--doctor")]
    Doctor,

    [EnumValue("--doctor-server")]
    DoctorServer,

    [EnumValue("--doctor-endpoint")]
    DoctorEndpoint,

    [EnumValue("--doctor-port")]
    DoctorPort,

    [EnumValue("--json")]
    Json,

    [EnumValue("--format")]
    Format,

    [EnumValue("--dry-run")]
    DryRun,

    [EnumValue("--yes")]
    [EnumValue("-y")]
    Yes,

    [EnumValue("--force")]
    [EnumValue("-f")]
    Force,

    [EnumValue("--quiet")]
    [EnumValue("-q")]
    Quiet,

    [EnumValue("--print")]
    Print,

    [EnumValue("--sdk-url")]
    SdkUrl,

    [EnumValue("--input-format")]
    InputFormat,

    [EnumValue("--output-format")]
    OutputFormat,

    [EnumValue("--replay-user-messages")]
    ReplayUserMessages,

    [EnumValue("--sandbox")]
    Sandbox,

    [EnumValue("--no-sandbox")]
    NoSandbox,

    [EnumValue("--debug-file")]
    DebugFile,

    [EnumValue("--session-timeout")]
    SessionTimeout,

    [EnumValue("--name")]
    Name,

    [EnumValue("--spawn")]
    Spawn,

    [EnumValue("--capacity")]
    Capacity,

    [EnumValue("--create-session-in-dir")]
    CreateSessionInDir,

    [EnumValue("--no-create-session-in-dir")]
    NoCreateSessionInDir,

    [EnumValue("--session-id")]
    SessionId,

    [EnumValue("--project")]
    [EnumValue("-p")]
    Project,
}
