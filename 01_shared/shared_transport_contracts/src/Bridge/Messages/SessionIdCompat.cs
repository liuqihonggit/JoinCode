namespace JoinCode.Transport.Bridge;

/// <summary>
/// Session ID 标签转换辅助 — 对齐 TS 端 sessionIdCompat.ts
/// CCR v2 兼容层: cse_* (基础设施层) ↔ session_* (客户端兼容 API)
/// </summary>
public static class SessionIdCompat
{
    private static Func<bool>? _isCseShimEnabled;

    /// <summary>
    /// 注册 cse_ shim 开关门控 — 对齐 TS 端 setCseShimGate
    /// 未注册时 shim 默认启用（与 TS 端 isCseShimEnabled() 默认值一致）
    /// 传入 null 重置为默认行为
    /// </summary>
    public static void SetCseShimGate(Func<bool>? gate)
    {
        _isCseShimEnabled = gate;
    }

    /// <summary>
    /// cse_* → session_* — 客户端兼容 API 使用
    /// Worker 端点 (/v1/code/sessions/{id}/worker/*) 使用 cse_*
    /// 客户端兼容端点 (/v1/sessions/{id}) 使用 session_*
    /// 非 cse_ 前缀的 ID 不转换
    /// </summary>
    public static string ToCompatSessionId(string id)
    {
        if (!id.StartsWith("cse_", StringComparison.Ordinal)) return id;
        if (_isCseShimEnabled is not null && !_isCseShimEnabled()) return id;
        return string.Concat("session_", id.AsSpan(4));
    }

    /// <summary>
    /// session_* → cse_* — 基础设施层调用使用
    /// toCompatSessionId 的逆操作
    /// 非 session_ 前缀的 ID 不转换
    /// </summary>
    public static string ToInfraSessionId(string id)
    {
        if (!id.StartsWith("session_", StringComparison.Ordinal)) return id;
        return string.Concat("cse_", id.AsSpan(8));
    }

    /// <summary>
    /// 跨前缀比较 — cse_xxx 和 session_xxx 视为同一会话
    /// 对齐 TS 端 workSecret.ts 的 sameSessionId
    /// </summary>
    public static bool SameSessionId(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;

        var aCore = StripPrefix(a);
        var bCore = StripPrefix(b);

        // 两个都有前缀且前缀不同，比较核心部分
        if (aCore.Length < a.Length && bCore.Length < b.Length)
        {
            return aCore.SequenceEqual(bCore);
        }

        return false;
    }

    /// <summary>去除 cse_ 或 session_ 前缀</summary>
    private static ReadOnlySpan<char> StripPrefix(string id)
    {
        if (id.StartsWith("cse_", StringComparison.Ordinal)) return id.AsSpan(4);
        if (id.StartsWith("session_", StringComparison.Ordinal)) return id.AsSpan(8);
        return id.AsSpan();
    }
}
