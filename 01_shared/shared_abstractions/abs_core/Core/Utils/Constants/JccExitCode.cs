namespace JoinCode.Abstractions.Utils;

/// <summary>
/// jcc 退出码枚举 — 源码生成器自动生成 JccExitCodeConstants + JccExitCodeExtensions + JccExitCodeHelpText
/// </summary>
public enum JccExitCode
{
    [EnumValue("0")]
    [SubCommandInfo("成功", "退出码")]
    Success,

    [EnumValue("1")]
    [SubCommandInfo("通用错误", "退出码")]
    GeneralError,

    [EnumValue("2")]
    [SubCommandInfo("配置错误", "退出码")]
    ConfigError,

    [EnumValue("3")]
    [SubCommandInfo("参数错误", "退出码")]
    ArgError,

    [EnumValue("4")]
    [SubCommandInfo("API Key 缺失", "退出码")]
    ApiKeyMissing,

    [EnumValue("10")]
    [SubCommandInfo("LLM 调用失败", "退出码")]
    LlmCallFailed,

    [EnumValue("11")]
    [SubCommandInfo("工具执行失败", "退出码")]
    ToolExecutionFailed,

    [EnumValue("12")]
    [SubCommandInfo("MCP 连接失败", "退出码")]
    McpConnectionFailed,

    [EnumValue("130")]
    [SubCommandInfo("用户中断 (Ctrl+C)", "退出码")]
    UserInterrupt,

    [EnumValue("1234")]
    [SubCommandInfo("--await 超时", "退出码")]
    AwaitTimeout,
}
