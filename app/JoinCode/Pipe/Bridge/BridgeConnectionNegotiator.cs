namespace JoinCode.Pipe;

public sealed class BridgeNegotiationResult
{
    public bool IsAccepted { get; init; }
    public string AgreedVersion { get; init; } = string.Empty;
    public string? RejectionReason { get; init; }
    public IReadOnlyList<string> CommonCapabilities { get; init; } = Array.Empty<string>();
}

[Register]
public sealed partial class BridgeConnectionNegotiator
{
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

    public Task<BridgeNegotiationResult> NegotiateAsync(
        string protocolVersion,
        IEnumerable<string>? capabilities = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);

        if (!SupportedVersions.Contains(protocolVersion))
        {
            return Task.FromResult(new BridgeNegotiationResult
            {
                IsAccepted = false,
                RejectionReason = $"Unsupported protocol version: {protocolVersion}. Supported: {string.Join(", ", SupportedVersions.OrderBy(v => v, VersionComparer))}"
            });
        }

        var remoteCapabilities = capabilities?.ToList() ?? new List<string>();

        var commonCapabilities = remoteCapabilities
            .Where(c => SupportedCapabilities.Contains(c))
            .ToList();

        if (commonCapabilities.Count == 0)
        {
            return Task.FromResult(new BridgeNegotiationResult
            {
                IsAccepted = false,
                RejectionReason = "No common capabilities found between client and server"
            });
        }

        return Task.FromResult(new BridgeNegotiationResult
        {
            IsAccepted = true,
            AgreedVersion = protocolVersion,
            CommonCapabilities = commonCapabilities
        });
    }

    private sealed class VersionComparerComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;

            if (Version.TryParse(x, out var vx) && Version.TryParse(y, out var vy))
            {
                return vx.CompareTo(vy);
            }

            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}