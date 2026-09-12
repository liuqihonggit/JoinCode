namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据实体
/// </summary>
public sealed class EvidenceRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Content { get; init; }
    public required EvidenceCategory Category { get; init; }
    public TrustLevel TrustLevel { get; set; } = TrustLevel.Moderate;
    public required AgentRole SubmittedBy { get; init; }
    public string? Source { get; init; }
    public string? SourceUrl { get; init; }
    public int? LineNumber { get; set; }
    public string? ExtractedText { get; set; }
    public bool IsUrlVerified { get; set; }
    public DateTime? UrlVerifiedAt { get; set; }
    public double Weight { get; init; } = 1.0;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<string> SupportingClaimIds { get; init; } = [];
    public List<string> RefutingClaimIds { get; init; } = [];
}
