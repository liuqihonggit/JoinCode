namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 终端捕获类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 CaptureTypeConstants + CaptureTypeExtensions
/// </summary>
public enum CaptureType
{
    /// <summary>屏幕快照</summary>
    [EnumValue("screen")] Screen = 0,

    /// <summary>缓冲区内容</summary>
    [EnumValue("buffer")] Buffer = 1
}
