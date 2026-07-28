namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 数据项 — 推理链中的基本单元
/// </summary>
public sealed class DataItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Content { get; init; }
    public DataState State { get; set; } = DataState.Assumption;
    public string? Source { get; init; }
    public int Confidence { get; set; } = 50;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public List<string> EvidenceIds { get; set; } = [];
    public List<string> CounterIds { get; set; } = [];
    public AgentRole? SubmittedBy { get; init; }
}
