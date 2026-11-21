namespace Core.Permission;

/// <summary>
/// 模型输出的原始形状 — 四字段全部可为空：模型可能漏字段、给错类型，
/// 校验一律放在 <see cref="PermissionExplainerPrompt.TryParse"/> 里统一做，
/// 而不是指望反序列化抛异常（缺字段不抛，会把半截结果当完整解释渲染出去）。
/// </summary>
internal sealed class PermissionExplanationDto {
    /// <summary>这条命令做什么。</summary>
    public string? Explanation { get; set; }

    /// <summary>为什么我要跑它。</summary>
    public string? Reasoning { get; set; }

    /// <summary>可能出什么问题。</summary>
    public string? Risk { get; set; }

    /// <summary>风险等级（LOW/MEDIUM/HIGH，来自基准字符串）。</summary>
    public string? RiskLevel { get; set; }
}

/// <summary>权限解释的 JSON 序列化上下文（AOT 兼容 — 只读一次响应，写方向不需要）。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(PermissionExplanationDto))]
internal partial class PermissionExplainerJsonContext : JsonSerializerContext;
