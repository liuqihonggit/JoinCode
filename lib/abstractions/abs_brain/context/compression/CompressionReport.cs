namespace JoinCode.Abstractions.Brain.Context.Compression;

public sealed record CompressionReportOptions(
    int OriginalTokenCount,
    int CompressedTokenCount,
    List<string> PreservedInfo,
    List<string> LostInfo,
    CompressionRequest? Request = null,
    string? StrategyName = null,
    long ProcessingTimeMs = 0,
    bool IsSuccess = true,
    string? ErrorMessage = null);

public sealed record CompressionReport
{
    public required string ReportId { get; init; }

    public required int OriginalTokenCount { get; init; }

    public required int CompressedTokenCount { get; init; }

    public required double CompressionRatio { get; init; }

    public required List<string> PreservedInfo { get; init; }

    public required List<string> LostInfo { get; init; }

    public required DateTime Timestamp { get; init; }

    public ContextLayerType? TargetLayer { get; init; }

    public string? StrategyName { get; init; }

    public long ProcessingTimeMs { get; init; }

    public CompressionRequest? Request { get; init; }

    public bool IsSuccess { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public int SavedTokens => OriginalTokenCount - CompressedTokenCount;

    public double CompressionEfficiency => OriginalTokenCount > 0
        ? (1 - CompressionRatio) * 100
        : 0;

    public static CompressionReport Create(CompressionReportOptions options)
    {
        var ratio = options.OriginalTokenCount > 0
            ? (double)options.CompressedTokenCount / options.OriginalTokenCount
            : 0;

        return new CompressionReport
        {
            ReportId = Guid.NewGuid().ToString("N"),
            OriginalTokenCount = options.OriginalTokenCount,
            CompressedTokenCount = options.CompressedTokenCount,
            CompressionRatio = ratio,
            PreservedInfo = options.PreservedInfo,
            LostInfo = options.LostInfo,
            Timestamp = DateTime.UtcNow,
            TargetLayer = options.Request?.TargetLayer,
            StrategyName = options.StrategyName,
            ProcessingTimeMs = options.ProcessingTimeMs,
            Request = options.Request,
            IsSuccess = options.IsSuccess,
            ErrorMessage = options.ErrorMessage
        };
    }

    public static CompressionReport CreateFailed(
        int originalTokenCount,
        string errorMessage,
        CompressionRequest? request = null)
    {
        return new CompressionReport
        {
            ReportId = Guid.NewGuid().ToString("N"),
            OriginalTokenCount = originalTokenCount,
            CompressedTokenCount = originalTokenCount,
            CompressionRatio = 1.0,
            PreservedInfo = new List<string>(),
            LostInfo = new List<string>(),
            Timestamp = DateTime.UtcNow,
            TargetLayer = request?.TargetLayer,
            Request = request,
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
