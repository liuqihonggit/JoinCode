namespace JoinCode.Abstractions.Utils;

/// <summary>
/// CLI 入口级子命令 — 源码生成器自动生成 CliSubCommandConstants + CliSubCommandExtensions
/// 适用范围: jcc [tool|agent|code|remote-control|rc|remote] [子参数]
///
/// 使用示例:
/// - FromValue("tool")           → CliSubCommand.Tool
/// - FromValue("RC")             → CliSubCommand.RemoteControl (OrdinalIgnoreCase)
/// - CliSubCommand.RemoteControl.ToValue() → "remote-control"
/// </summary>
public enum CliSubCommand
{
    /// <summary>MCP 工具管理</summary>
    [EnumValue("tool")] Tool,

    /// <summary>智能体管理</summary>
    [EnumValue("agent")] Agent,

    /// <summary>代码操作</summary>
    [EnumValue("code")] Code,

    /// <summary>远程控制（主名称）</summary>
    [EnumValue("remote-control")] RemoteControl,

    /// <summary>远程控制别名</summary>
    [EnumValue("rc")] Rc,

    /// <summary>远程控制别名</summary>
    [EnumValue("remote")] Remote,
}
