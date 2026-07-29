namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 上下文裁剪模式枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 SnipModeConstants + SnipModeExtensions
/// </summary>
public enum SnipMode
{
    /// <summary>回退上一轮</summary>
    [EnumValue("rewind")] Rewind = 0,

    /// <summary>回退到指定消息索引</summary>
    [EnumValue("rewind_to")] RewindTo = 1,

    /// <summary>清空全部</summary>
    [EnumValue("clear")] Clear = 2
}
