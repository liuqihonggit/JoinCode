
namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 法官 LLM 裁决响应 JSON 格式 — 顶层包装，对应 SystemPrompt 中声明的 verdicts 数组结构
/// </summary>
public sealed class JudgeVerdictsJson {
    /// <summary>裁决列表</summary>
    [JsonPropertyName("verdicts")]
    public List<JudgeVerdictItemJson> Verdicts { get; set; } = new();
}

/// <summary>
/// 法官单条裁决项 JSON 格式 — claimContent/decision/reason/confidence 四字段
/// </summary>
public sealed class JudgeVerdictItemJson {
    /// <summary>假定内容（用于匹配 pending 项）</summary>
    [JsonPropertyName("claimContent")]
    public string? ClaimContent { get; set; }

    /// <summary>裁决决策（Accept|Reject|PartiallyAccept|Pending）</summary>
    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    /// <summary>裁决理由</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>置信度（0-100），缺失时由调用方补默认值 50</summary>
    [JsonPropertyName("confidence")]
    public int? Confidence { get; set; }
}

/// <summary>
/// 控方 LLM 证据响应 JSON 格式 — 顶层包装，对应 SystemPrompt 中声明的 evidence 数组结构
/// </summary>
public sealed class ProsecutorEvidenceJson {
    /// <summary>证据列表</summary>
    [JsonPropertyName("evidence")]
    public List<LlmEvidenceItemJson> Evidence { get; set; } = new();
}

/// <summary>
/// 辩方 LLM 反驳响应 JSON 格式 — 顶层包装，含反驳证据与质疑，对应 SystemPrompt 中声明的 counterEvidence + doubts 结构
/// </summary>
public sealed class DefenderCounterEvidenceJson {
    /// <summary>反驳证据列表</summary>
    [JsonPropertyName("counterEvidence")]
    public List<LlmEvidenceItemJson> CounterEvidence { get; set; } = new();

    /// <summary>质疑列表（字符串数组）</summary>
    [JsonPropertyName("doubts")]
    public List<string> Doubts { get; set; } = new();
}

/// <summary>
/// LLM 证据项通用 JSON 格式 — 控方 evidence 与辩方 counterEvidence 共享同一结构（content/source/trustLevel/weight）
/// </summary>
public sealed class LlmEvidenceItemJson {
    /// <summary>证据内容描述</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>证据来源</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>信任度（DirectEvidence|StrongCorroboration|Moderate|Weak|Hearsay|Unreliable）</summary>
    [JsonPropertyName("trustLevel")]
    public string? TrustLevel { get; set; }

    /// <summary>权重（0.1-10.0），缺失时由调用方补默认值 1.0</summary>
    [JsonPropertyName("weight")]
    public double? Weight { get; set; }
}
