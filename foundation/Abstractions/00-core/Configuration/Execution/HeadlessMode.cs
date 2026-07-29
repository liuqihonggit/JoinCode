namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 无头模式检测枚举 — 标识 CLI 运行环境的交互能力
/// </summary>
public enum HeadlessMode : byte
{
    [EnumValue("interactive")][DisplayText("交互")] Interactive,
    [EnumValue("userRequested")][DisplayText("用户请求")] UserRequested,
    [EnumValue("noTty")][DisplayText("无TTY")] NoTty,
    [EnumValue("ciEnvironment")][DisplayText("CI环境")] CiEnvironment
}
