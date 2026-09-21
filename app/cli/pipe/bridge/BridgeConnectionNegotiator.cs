namespace JoinCode.Pipe;

/// <summary>Bridge 连接协商结果 — 描述协议版本与能力集协商的最终结论</summary>
public sealed class BridgeNegotiationResult {
    /// <summary>是否协商成功（被接受）</summary>
    public bool IsAccepted { get; init; }
    /// <summary>协商一致的协议版本；协商失败时为空字符串</summary>
    public string AgreedVersion { get; init; } = string.Empty;
    /// <summary>协商失败时的拒绝原因；成功时为 null</summary>
    public string? RejectionReason { get; init; }
    /// <summary>客户端与服务端共同支持的能力列表</summary>
    public IReadOnlyList<string> CommonCapabilities { get; init; } = Array.Empty<string>();
}

/// <summary>Bridge 连接协商器 — 校验协议版本并计算双方共同支持的能力集，单例服务</summary>
[Register(typeof(BridgeConnectionNegotiator), ServiceLifetime.Singleton)]
public sealed partial class BridgeConnectionNegotiator : ServiceEntity {
    private static readonly FrozenSet<string> SupportedVersions = new HashSet<string>(StringComparer.Ordinal)
    {
        "1.0", "1.1", "2.0"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SupportedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "basic",
        "reconnect",
        "compress",
        "encryption",
        "advanced"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly VersionComparerComparer VersionComparer = new();

    /// <summary>协商 Bridge 连接参数 — 校验协议版本并计算共同能力集</summary>
    /// <param name="protocolVersion">客户端请求的协议版本</param>
    /// <param name="capabilities">客户端声明的能力列表；null 视为空集</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>协商结果，包含是否接受、一致版本、共同能力集或拒绝原因</returns>
    public Task<BridgeNegotiationResult> NegotiateAsync(
        string protocolVersion,
        IEnumerable<string>? capabilities = null,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);

        if (!SupportedVersions.Contains(protocolVersion)) {
            return Task.FromResult(new BridgeNegotiationResult {
                IsAccepted = false,
                RejectionReason = $"Unsupported protocol version: {protocolVersion}. Supported: {string.Join(", ", SupportedVersions.OrderBy(v => v, VersionComparer))}"
            });
        }

        var remoteCapabilities = capabilities?.ToList() ?? new List<string>();

        var commonCapabilities = remoteCapabilities
            .Where(c => SupportedCapabilities.Contains(c))
            .ToList();

        if (commonCapabilities.Count == 0) {
            return Task.FromResult(new BridgeNegotiationResult {
                IsAccepted = false,
                RejectionReason = "No common capabilities found between client and server"
            });
        }

        return Task.FromResult(new BridgeNegotiationResult {
            IsAccepted = true,
            AgreedVersion = protocolVersion,
            CommonCapabilities = commonCapabilities
        });
    }

    private sealed class VersionComparerComparer : IComparer<string> {
        /// <summary>比较两个版本字符串 — 优先按语义版本比较，无法解析时回退到序数字字符串比较</summary>
        /// <param name="x">第一个版本字符串</param>
        /// <param name="y">第二个版本字符串</param>
        /// <returns>小于零表示 x 小于 y，零表示相等，大于零表示 x 大于 y</returns>
        public int Compare(string? x, string? y) {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;

            if (Version.TryParse(x, out var vx) && Version.TryParse(y, out var vy)) {
                return vx.CompareTo(vy);
            }

            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}