namespace Core.Configuration.ModelFetch;

/// <summary>
/// 远程模型信息 — 承载供应商 /models 端点返回的完整元数据
/// <para>解析自 OpenAI 兼容 /v1/models 响应的 data[] 数组每一项</para>
/// <para>字段映射: id→Id, description→Description, context_length→ContextLength,</para>
/// <para>max_output_length→MaxOutputLength, input_modalities→InputModalities,</para>
/// <para>output_modalities→OutputModalities, supported_features→SupportedFeatures</para>
/// </summary>
public sealed class RemoteModelInfo
{
    /// <summary>模型 ID — 唯一标识，如 "sensenova-6.8-flash-lite"</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>模型描述 — 来自 API description 字段，空表示未返回</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>上下文窗口长度 — 来自 API context_length 字段，0 表示未知</summary>
    public int ContextLength { get; set; }

    /// <summary>最大输出长度 — 来自 API max_output_length 字段，0 表示未知</summary>
    public int MaxOutputLength { get; set; }

    /// <summary>输入模态列表 — 来自 API input_modalities 字段，如 ["text","image"]</summary>
    public IReadOnlyList<string> InputModalities { get; set; } = [];

    /// <summary>输出模态列表 — 来自 API output_modalities 字段，如 ["text"] 或 ["image"]</summary>
    public IReadOnlyList<string> OutputModalities { get; set; } = [];

    /// <summary>支持的特性列表 — 来自 API supported_features 字段，如 ["tools","json_mode","reasoning"]</summary>
    public IReadOnlyList<string> SupportedFeatures { get; set; } = [];
}
