
namespace Core.Bridge;

/// <summary>
/// Webhook 载荷清洗 — 对齐 TS 端 webhookSanitizer.ts
/// TS 端当前为空实现（恒等函数），CS 端对齐
/// </summary>
public static class BridgeWebhookSanitizer
{
    /// <summary>
    /// 清洗 Webhook 载荷 — 当前为占位桩函数，直接返回原值
    /// 对齐 TS 端 sanitizeWebhookPayload（空实现）
    /// </summary>
    public static T SanitizeWebhookPayload<T>(T value) => value;
}
