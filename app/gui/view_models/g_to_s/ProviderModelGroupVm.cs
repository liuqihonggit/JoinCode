namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 供应商模型分组 — 模型选择器 Popup 的数据源,按供应商分组展示模型列表。
/// 每组含供应商图标信息(首字母+品牌色)和该供应商下所有可选模型。
/// </summary>
public sealed class ProviderModelGroupVm {
    /// <summary>供应商 ID(如 openai/deepseek/anthropic)</summary>
    public required string ProviderId { get; init; }

    /// <summary>供应商显示名(如 OpenAI/DeepSeek/Anthropic)</summary>
    public required string ProviderName { get; init; }

    /// <summary>供应商首字母(用于圆形图标文字,如 O/D/A)</summary>
    public required string Initial { get; init; }

    /// <summary>供应商品牌色(用于圆形图标背景,#RRGGBB 格式)</summary>
    public required string BrandColor { get; init; }

    /// <summary>该供应商下所有可选模型列表</summary>
    public required IReadOnlyList<ProviderModelEntryVm> Models { get; init; }
}

/// <summary>
/// 供应商模型条目 — 模型选择器 Popup 中单个模型项,含模型 ID、显示名、上下文窗口大小。
/// </summary>
public sealed class ProviderModelEntryVm {
    /// <summary>模型 ID(写入配置的真实标识)</summary>
    public required string ModelId { get; init; }

    /// <summary>模型显示名(如 "GPT-4o"、"DeepSeek V4 Flash")</summary>
    public required string DisplayName { get; init; }

    /// <summary>上下文窗口大小(token 数,0 表示未知)</summary>
    public int ContextWindow { get; init; }

    /// <summary>选择键(格式 "providerId|modelId")— 传给 SelectModelFromPopupCommand</summary>
    public required string SelectionKey { get; init; }

    /// <summary>上下文窗口显示文本(如 "1M"、"128K"、"" 表示未知)</summary>
    public string ContextWindowDisplay => ContextWindow switch {
        >= 1_048_576 => $"{ContextWindow / 1_048_576}M",
        >= 1024 => $"{ContextWindow / 1024}K",
        > 0 => ContextWindow.ToString(),
        _ => ""
    };
}
