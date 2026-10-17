namespace Services.Web;

/// <summary>
/// 网络守卫判定结果
/// </summary>
/// <param name="Allowed">是否放行</param>
/// <param name="Reason">拒绝原因(Allowed=true 时为 null)</param>
public sealed record NetworkGuardDecision(bool Allowed, string? Reason);

/// <summary>
/// SSRF 私有网络地址守卫 — 阻止请求访问内网/链路本地/回环地址
/// 对齐 Reasonix web_fetch SSRF 防护：在 DNS 解析后检查解析后的 IP 地址
/// </summary>
public static class PrivateNetworkGuard {
    private static readonly byte[] LoopbackV4 = { 127, 0, 0, 0 };
    private static readonly byte[] LinkLocalV4Prefix = { 169, 254 };
    private static readonly byte[] Private10V4Prefix = { 10 };
    private static readonly byte[] Private172V4Prefix = { 172 };
    private static readonly byte[] Private192V4Prefix = { 192, 168 };

    /// <summary>
    /// 判断指定 IP 地址是否属于私有/保留地址（回环、链路本地、内网、组播等）。
    /// </summary>
    /// <param name="address">待检查的 IP 地址。</param>
    /// <returns>属于私有/保留地址返回 true，否则返回 false。</returns>
    public static bool IsPrivateAddress(IPAddress address) {        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPrivateV4(address.GetAddressBytes());

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsPrivateV6(address.GetAddressBytes());

        return false;
    }

    /// <summary>
    /// 校验主机地址是否可放行 — SSRF 统一入口(唯一数据源,双闸/重定向复检复用, ADR 0140)
    /// <para>
    /// fail-closed 策略: DNS 解析失败 → 拒绝(禁止放行未验证的主机);
    /// 多解析结果中任一私网 IP → 拒绝; IP 字面量 → 直接判定不触发 DNS。
    /// </para>
    /// </summary>
    /// <param name="host">主机名或 IP 字面量</param>
    /// <returns>判定结果(Allowed=false 时携带面向用户的引导文案)</returns>
    public static NetworkGuardDecision ValidateHost(string host) =>
        ValidateHost(host, Dns.GetHostAddresses);

    /// <summary>
    /// 带可注入 DNS 解析器的校验入口 — 惰性注入仅测试使用,生产调用无参重载
    /// </summary>
    /// <param name="host">主机名或 IP 字面量</param>
    /// <param name="dnsResolver">DNS 解析函数</param>
    public static NetworkGuardDecision ValidateHost(string host, Func<string, IPAddress[]> dnsResolver) {
        if (string.IsNullOrWhiteSpace(host)) {
            return new NetworkGuardDecision(false, "SSRF protection: empty host is not allowed.");
        }

        if (IPAddress.TryParse(host, out var directIp)) {
            return PrivateNetworkGuard.IsPrivateAddress(directIp)
                ? new NetworkGuardDecision(false,
                    $"SSRF protection: direct IP address {host} is a private/reserved address. Access to internal networks is blocked.")
                : new NetworkGuardDecision(true, null);
        }

        IPAddress[] addresses;
        try {
            addresses = dnsResolver(host);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // fail-closed: 解析失败的主机不可验证,拒绝放行(原缺陷 SocketException 被吞后放行)
            return new NetworkGuardDecision(false,
                $"SSRF protection: DNS resolution failed for host {host} ({ex.GetType().Name}), request is denied (fail-closed). " +
                "Unresolvable hosts cannot be verified as safe — check the URL or use a resolvable hostname.");
        }

        if (addresses.Length == 0) {
            return new NetworkGuardDecision(false,
                $"SSRF protection: host {host} resolved to zero addresses, request is denied (fail-closed).");
        }

        foreach (var addr in addresses) {
            if (PrivateNetworkGuard.IsPrivateAddress(addr)) {
                return new NetworkGuardDecision(false,
                    $"SSRF protection: hostname {host} resolves to private/reserved address {addr}. " +
                    "Access to internal networks is blocked.");
            }
        }

        return new NetworkGuardDecision(true, null);
    }

    private static bool IsPrivateV4(byte[] bytes) {
        if (StartsWith(bytes, LoopbackV4, 8))
            return true;

        if (StartsWith(bytes, LinkLocalV4Prefix))
            return true;

        if (StartsWith(bytes, Private10V4Prefix))
            return true;

        if (StartsWith(bytes, Private172V4Prefix) && bytes[1] is >= 16 and <= 31)
            return true;

        if (StartsWith(bytes, Private192V4Prefix))
            return true;

        if (bytes[0] == 0)
            return true;

        return false;
    }

    private static bool IsPrivateV6(byte[] bytes) {
        var isLoopback = bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
                         && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0
                         && bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0
                         && bytes[12] == 0 && bytes[13] == 0 && bytes[14] == 0 && bytes[15] == 1;
        if (isLoopback)
            return true;

        if (bytes[0] == 0xfc || bytes[0] == 0xfd)
            return true;

        if (bytes[0] == 0xfe && (bytes[1] & 0xC0) == 0x80)
            return true;

        if (bytes[0] == 0xff)
            return true;

        return false;
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix, int maskBits = -1) {
        if (bytes.Length < prefix.Length)
            return false;

        if (maskBits < 0) {
            for (var i = 0; i < prefix.Length; i++) {
                if (bytes[i] != prefix[i])
                    return false;
            }
            return true;
        }

        var fullBytes = maskBits / 8;
        var remainingBits = maskBits % 8;

        for (var i = 0; i < fullBytes && i < prefix.Length; i++) {
            if (bytes[i] != prefix[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < prefix.Length) {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((bytes[fullBytes] & mask) != (prefix[fullBytes] & mask))
                return false;
        }

        return true;
    }
}