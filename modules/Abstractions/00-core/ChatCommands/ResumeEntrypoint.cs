namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /resume 恢复入口类型
/// 对齐 TS: src/types/command.ts — ResumeEntrypoint
/// 用于追踪用户通过哪种方式恢复了会话
/// </summary>
public enum ResumeEntrypoint
{
    /// <summary>--resume CLI 参数</summary>
    [EnumValue("cli_flag")] CliFlag,

    /// <summary>/resume 命令选择器（交互式列表选择）</summary>
    [EnumValue("slash_command_picker")] SlashCommandPicker,

    /// <summary>/resume &lt;session-id&gt; 直接指定会话ID</summary>
    [EnumValue("slash_command_session_id")] SlashCommandSessionId,

    /// <summary>/resume &lt;custom-title&gt; 按自定义标题搜索</summary>
    [EnumValue("slash_command_title")] SlashCommandTitle,

    /// <summary>fork 会话</summary>
    [EnumValue("fork")] Fork,
}
