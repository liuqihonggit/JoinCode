namespace JoinCode.Abstractions.Utils;

/// <summary>
/// ChatService 循环后端 — TASK103 灰度切换的两态。
/// </summary>
public enum ChatLoopBackend {
    /// <summary>旧 QueryLoopMiddleware 循环（逃生开关，JCC_CHAT_LOOP_BACKEND=legacy 显式启用）。</summary>
    [EnumValue("legacy")] Legacy,

    /// <summary>路由到 QueryEngine 的新后端（TASK103 默认）。</summary>
    [EnumValue("query")] Query,
}

/// <summary>
/// 聊天循环后端策略 — JCC_CHAT_LOOP_BACKEND 的唯一解析点。
/// <para>
/// 默认 query（TASK103 已完成已知差距迁移并翻转默认）；
/// 宿主异常时可用 JCC_CHAT_LOOP_BACKEND=legacy 逃生回旧循环。
/// </para>
/// </summary>
public static class ChatLoopBackendPolicy {
    /// <summary>
    /// 读取循环后端 — 未设置/非法值回退默认 query，兼容 1/0/true/false/on/off 别名。
    /// </summary>
    /// <returns>解析后的循环后端。</returns>
    public static ChatLoopBackend Read() {
        var raw = Environment.GetEnvironmentVariable(JccEnvVar.ChatLoopBackend.ToValue());
        if (string.IsNullOrWhiteSpace(raw))
            return ChatLoopBackend.Query;

        var value = raw.Trim().ToLowerInvariant();
        if (value is "1" or "true" or "on" or "query")
            return ChatLoopBackend.Query;
        if (value is "0" or "false" or "off" or "legacy")
            return ChatLoopBackend.Legacy;

        return ChatLoopBackendExtensions.FromValue(value) ?? ChatLoopBackend.Query;
    }

    /// <summary>
    /// 是否路由到 QueryEngine 后端。
    /// </summary>
    public static bool UseQueryEngine => Read() == ChatLoopBackend.Query;
}
