namespace JoinCode.Abstractions.Utils;

/// <summary>
/// PR订阅操作类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 PrSubscriptionActionConstants + PrSubscriptionActionExtensions
/// </summary>
public enum PrSubscriptionAction
{
    /// <summary>订阅PR</summary>
    [EnumValue("subscribe")] Subscribe = 0,

    /// <summary>取消订阅</summary>
    [EnumValue("unsubscribe")] Unsubscribe = 1,

    /// <summary>列出订阅</summary>
    [EnumValue("list")] List = 2
}
