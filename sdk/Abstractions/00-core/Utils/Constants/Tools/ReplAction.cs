namespace JoinCode.Abstractions.Utils;

/// <summary>
/// REPL 操作类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ReplActionConstants + ReplActionExtensions
/// </summary>
public enum ReplAction
{
    /// <summary>执行代码</summary>
    [EnumValue("execute")] Execute = 0,

    /// <summary>启用REPL模式</summary>
    [EnumValue("enable")] Enable = 1,

    /// <summary>禁用REPL模式</summary>
    [EnumValue("disable")] Disable = 2,

    /// <summary>查看REPL状态</summary>
    [EnumValue("status")] Status = 3
}
