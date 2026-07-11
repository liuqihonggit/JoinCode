namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 消息状态枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 MessageStatusConstants + MessageStatusExtensions
/// </summary>
public enum MessageStatus
{
    /// <summary>主动推送</summary>
    [EnumValue("proactive")] Proactive = 0,

    /// <summary>普通回复</summary>
    [EnumValue("normal")] Normal = 1
}
