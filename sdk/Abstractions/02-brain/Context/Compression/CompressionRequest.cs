namespace JoinCode.Abstractions.Brain.Context.Compression;

public sealed record CompressionRequest
{
    public ContextLayerType TargetLayer { get; init; } = ContextLayerType.Summary;

    public int CompressionLevel { get; init; } = 3;

    public List<string> PreserveKeywords { get; init; } = new();

    public int MaxOutputTokens { get; init; } = 4000;

    public ContentType ContentType { get; init; } = ContentType.Text;

    public bool PreserveKeyDecisions { get; init; } = true;

    public bool PreserveSignatures { get; init; } = true;

    public bool PreserveImports { get; init; } = true;

    public bool UseSummarization { get; init; } = true;

    public int TokenThreshold { get; init; } = 8000;

    public int MinCompressionThreshold { get; init; } = WorkflowConstants.ContextCompression.MinCompressionThreshold;

    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public double GetTargetCompressionRatio() => CompressionLevel switch
    {
        1 => 0.8,
        2 => 0.6,
        3 => 0.5,
        4 => 0.35,
        5 => 0.2,
        _ => 0.5
    };

    public CompressionOptions ToCompressionOptions()
    {
        return new CompressionOptions
        {
            TargetCompressionRatio = GetTargetCompressionRatio(),
            MaxOutputTokens = MaxOutputTokens,
            PreserveSignatures = PreserveSignatures,
            PreserveImports = PreserveImports,
            UseSummarization = UseSummarization,
            PreserveKeyDecisions = PreserveKeyDecisions,
            MinCompressionThreshold = MinCompressionThreshold
        };
    }

    public static CompressionRequest Light(ContextLayerType targetLayer = ContextLayerType.Summary)
    {
        return new CompressionRequest
        {
            TargetLayer = targetLayer,
            CompressionLevel = 1,
            MaxOutputTokens = 8000,
            UseSummarization = false
        };
    }

    public static CompressionRequest Standard(ContextLayerType targetLayer = ContextLayerType.Summary)
    {
        return new CompressionRequest
        {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            MaxOutputTokens = 4000,
            UseSummarization = true
        };
    }

    public static CompressionRequest Aggressive(ContextLayerType targetLayer = ContextLayerType.Index)
    {
        return new CompressionRequest
        {
            TargetLayer = targetLayer,
            CompressionLevel = 5,
            MaxOutputTokens = 2000,
            UseSummarization = true,
            PreserveSignatures = true,
            PreserveImports = false
        };
    }

    public static CompressionRequest ForCode(ContextLayerType targetLayer = ContextLayerType.Summary)
    {
        return new CompressionRequest
        {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            ContentType = ContentType.Code,
            MaxOutputTokens = 4000,
            PreserveSignatures = true,
            PreserveImports = true,
            UseSummarization = false
        };
    }

    public static CompressionRequest ForDialogue(ContextLayerType targetLayer = ContextLayerType.Summary)
    {
        return new CompressionRequest
        {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            ContentType = ContentType.Dialogue,
            MaxOutputTokens = 4000,
            UseSummarization = true,
            PreserveKeyDecisions = true
        };
    }
}
