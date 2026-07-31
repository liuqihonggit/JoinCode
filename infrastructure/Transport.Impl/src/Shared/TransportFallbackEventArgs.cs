namespace JoinCode.Transport;

public sealed class TransportFallbackEventArgs : EventArgs
{
    public required string FromTransportType { get; init; }
    public required string ToTransportType { get; init; }
    public required string Reason { get; init; }
    public required bool IsServerSide { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public int FromPriority { get; init; }
    public int ToPriority { get; init; }
}
