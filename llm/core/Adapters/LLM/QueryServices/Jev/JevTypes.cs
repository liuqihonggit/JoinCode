
namespace Api.LLM;

/// <summary>
/// Jev API 请求/响应 DTO — TypeSafe AI System One Model
/// 端点: POST https://api.typesafe.ai/v1/systemone
/// 与 OpenAI Chat Completions 根本不同:state+questions 替代 messages,返回类型化决策而非文本
/// </summary>
internal sealed class JevRequest {
    /// <summary>获取或设置模型标识。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>输入材料(上下文) — 字符串/JSON 对象/数组,JsonElement 承载任意 JSON 值</summary>
    [JsonPropertyName("state")]
    public JsonElement State { get; set; }

    /// <summary>具名问题字典,key 为问题标识</summary>
    [JsonPropertyName("questions")]
    public Dictionary<string, JevQuestion> Questions { get; set; } = new();
}

/// <summary>
/// Jev 问题定义 — 对应请求 questions 字典的 value
/// </summary>
internal sealed class JevQuestion {
    /// <summary>决策原语类型:"noul"(是非概率)/ "choice"(分类)/ "score"(评分)</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>问题指令 — 告诉模型如何判断/分类/评分</summary>
    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    /// <summary>选项列表(仅 Choice 类型需要,Noul/Score 为空集合)</summary>
    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = [];
}

/// <summary>
/// Jev API 响应 DTO
/// </summary>
internal sealed class JevResponse {
    /// <summary>获取或设置响应标识。</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>获取或设置模型标识。</summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>决策答案字典,key 对应请求 questions 的 key</summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, JevAnswer> Answers { get; set; } = new();

    /// <summary>获取或设置 token 用量。</summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JevUsage? Usage { get; set; }
}

/// <summary>
/// Jev 决策答案 — 三种原语互斥(Noul/Choice/Score),confidence 通用
/// </summary>
internal sealed class JevAnswer {
    /// <summary>Noul 原语:答案为"是"的估计概率(0.0-1.0)</summary>
    [JsonPropertyName("noul")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Noul { get; set; }

    /// <summary>Choice 原语:选中的选项标识</summary>
    [JsonPropertyName("choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Choice { get; set; }

    /// <summary>Score 原语:评分值</summary>
    [JsonPropertyName("score")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Score { get; set; }

    /// <summary>置信度(0.0-1.0),模型对该决策的自信程度</summary>
    [JsonPropertyName("confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Confidence { get; set; }
}

/// <summary>
/// Jev token 用量
/// </summary>
internal sealed class JevUsage {
    /// <summary>获取或设置输入令牌数。</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }
}
