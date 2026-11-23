namespace McpProtocol;

/// <summary>
/// MCP serve 鉴权令牌 — 生成与校验的唯一数据源（ADR 0138: mcp serve 默认 token 鉴权）。
/// <para>生成: CSPRNG 32 字节 → 64 位 hex（与 Mcp-Session-Id 同强度）。</para>
/// <para>校验: 接受 <c>Authorization: Bearer &lt;token&gt;</c> 或 <c>Mcp-Token: &lt;token&gt;</c>，
/// 常量时间比较（<see cref="CryptographicOperations.FixedTimeEquals"/>）防时序侧信道。</para>
/// <para>禁止关闭: 显式传入空 token → <see cref="ArgumentException"/>，无豁免开关。</para>
/// </summary>
public sealed class McpAuthToken {
    /// <summary>令牌值（64 位 hex 或用户显式提供的值）</summary>
    public string Value { get; }

    private McpAuthToken(string value) {
        Value = value;
    }

    /// <summary>
    /// 显式构造令牌对象（脚本/集成场景，服务端与客户端约定一致）
    /// </summary>
    /// <param name="token">非空 token 值</param>
    /// <exception cref="ArgumentException">token 为空或空白</exception>
    public static McpAuthToken For(string token) {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new McpAuthToken(token);
    }

    /// <summary>
    /// 自动生成 64 位 hex 随机 token（CSPRNG）
    /// </summary>
    /// <returns>token 字符串</returns>
    public static string Generate() {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>自动生成随机令牌对象</summary>
    public static McpAuthToken New() {
        return new McpAuthToken(Generate());
    }

    /// <summary>
    /// 校验请求头中的 token — 不区分 header 名与 scheme 大小写
    /// </summary>
    /// <param name="headers">请求头集合（key 不区分大小写）</param>
    /// <param name="token">服务端 token 值</param>
    /// <param name="reason">失败原因与引导文案；成功时为 null</param>
    /// <returns>校验通过返回 true；否则 false</returns>
    public static bool TryValidate(IEnumerable<KeyValuePair<string, string>> headers, string token, out string? reason) {
        var expectedMcpToken = token;
        string? bearerCandidate = null;
        string? mcpTokenCandidate = null;
        foreach (var header in headers) {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) {
                bearerCandidate = header.Value;
            } else if (header.Key.Equals("Mcp-Token", StringComparison.OrdinalIgnoreCase)) {
                mcpTokenCandidate = header.Value;
            }
        }

        if (TryMatchBearer(bearerCandidate, expectedMcpToken)) {
            reason = null;
            return true;
        }

        if (IsMatch(mcpTokenCandidate, expectedMcpToken)) {
            reason = null;
            return true;
        }

        reason = bearerCandidate is null && mcpTokenCandidate is null
            ? "缺少鉴权: 服务端已启用 token 鉴权。请求头加 'Authorization: Bearer <token>' 或 'Mcp-Token: <token>'，token 见 serve 启动输出的 Mcp-Token 行"
            : "token 不匹配: 请求头中的 token 与服务端不一致，请核对 serve 启动输出 Mcp-Token 行的最新值";
        return false;
    }

    /// <summary>Authorization 头必须是 "Bearer &lt;token&gt;" 格式（scheme 大小写不敏感）</summary>
    private static bool TryMatchBearer(string? headerValue, string token) {
        if (string.IsNullOrEmpty(headerValue))
            return false;
        var parts = headerValue.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            return false;
        return IsMatch(parts[1], token);
    }

    /// <summary>UTF-8 字节常量时间比较（长度不等时立即返回，长度非秘密）</summary>
    private static bool IsMatch(string? candidate, string token) {
        if (string.IsNullOrEmpty(candidate))
            return false;
        var expected = System.Text.Encoding.UTF8.GetBytes(token);
        var actual = System.Text.Encoding.UTF8.GetBytes(candidate);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
