namespace JoinCode.Abstractions.Utils;

/// <summary>
/// ChatService 循环后端 — TASK103 灰度切换的两态。
/// </summary>
public enum ChatLoopBackend {
    /// <summary>旧 QueryLoopMiddleware 循环（默认，功能完整）。</summary>
    [EnumValue("legacy")] Legacy,

    /// <summary>路由到 QueryEngine 的新后端（合并目标，灰度验证中）。</summary>
    [EnumValue("query")] Query,
}

/// <summary>
/// 聊天循环后端策略 — JCC_CHAT_LOOP_BACKEND 的唯一解析点。
/// <para>
/// 默认 legacy：新后端能力未迁移完成前不得改变既有宿主行为；
/// 灰度期间仅显式设置 query 才切换，便于逐宿主验证后翻转默认值。
/// </para>
/// </summary>
public static class ChatLoopBackendPolicy {
    /// <summary>
    /// 读取循环后端 — 未设置/非法值回退 legacy，兼容 1/0/true/false/on/off 别名。
    /// </summary>
    /// <returns>解析后的循环后端。</returns>
    public static ChatLoopBackend Read() {
        var raw = Environment.GetEnvironmentVariable(JccEnvVar.ChatLoopBackend.ToValue());
        if (string.IsNullOrWhiteSpace(raw))
            return ChatLoopBackend.Legacy;

        var value = raw.Trim().ToLowerInvariant();
        if (value is "1" or "true" or "on" or "query")
            return ChatLoopBackend.Query;
        if (value is "0" or "false" or "off" or "legacy")
            return ChatLoopBackend.Legacy;

        return ChatLoopBackendExtensions.FromValue(value) ?? ChatLoopBackend.Legacy;
    }

    /// <summary>
    /// 是否路由到 QueryEngine 后端。
    /// </summary>
    public static bool UseQueryEngine => Read() == ChatLoopBackend.Query;
}
