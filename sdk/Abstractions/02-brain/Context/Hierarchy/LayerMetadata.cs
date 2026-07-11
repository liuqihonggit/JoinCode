namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public record LayerMetadata
{
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? CompressedAt { get; init; }

    public int OriginalTokenCount { get; init; }

    public int CompressedTokenCount { get; init; }

    public double CompressionRatio => OriginalTokenCount > 0
        ? (CompressedTokenCount > 0 ? (double)CompressedTokenCount / OriginalTokenCount : 1.0)
        : 1.0;

    public string LayerName { get; init; } = string.Empty;

    public LayerMetadata() { }

    public LayerMetadata(string layerName)
    {
        LayerName = layerName;
    }

    public LayerMetadata WithCompression(int compressedTokenCount)
    {
        return this with
        {
            CompressedAt = DateTime.UtcNow,
            CompressedTokenCount = compressedTokenCount
        };
    }
}
