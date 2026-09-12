namespace Services.Web;

/// <summary>
/// SSRF 私有网络地址守卫 — 阻止请求访问内网/链路本地/回环地址
/// 对齐 Reasonix web_fetch SSRF 防护：在 DNS 解析后检查解析后的 IP 地址
/// </summary>
public static class PrivateNetworkGuard
{
    private static readonly byte[] LoopbackV4 = { 127, 0, 0, 0 };
    private static readonly byte[] LinkLocalV4Prefix = { 169, 254 };
    private static readonly byte[] Private10V4Prefix = { 10 };
    private static readonly byte[] Private172V4Prefix = { 172 };
    private static readonly byte[] Private192V4Prefix = { 192, 168 };

    public static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPrivateV4(address.GetAddressBytes());

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsPrivateV6(address.GetAddressBytes());

        return false;
    }

    private static bool IsPrivateV4(byte[] bytes)
    {
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

    private static bool IsPrivateV6(byte[] bytes)
    {
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

    private static bool StartsWith(byte[] bytes, byte[] prefix, int maskBits = -1)
    {
        if (bytes.Length < prefix.Length)
            return false;

        if (maskBits < 0)
        {
            for (var i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i])
                    return false;
            }
            return true;
        }

        var fullBytes = maskBits / 8;
        var remainingBits = maskBits % 8;

        for (var i = 0; i < fullBytes && i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < prefix.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((bytes[fullBytes] & mask) != (prefix[fullBytes] & mask))
                return false;
        }

        return true;
    }
}
