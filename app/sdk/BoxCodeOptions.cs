namespace JoinCode.Sdk;

/// <summary>
/// JoinCode SDK 配置选项 — 外部宿主通过 AddJoinCode 扩展方法注入。
/// </summary>
public sealed class JoinCodeOptions {
    /// <summary>LLM 供应商类型，默认 OpenAi。</summary>
    public VendorKind Vendor { get; set; } = VendorKind.OpenAi;

    /// <summary>模型 ID（如 gpt-4o、deepseek-chat）。</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>API Key，可选（也可通过环境变量提供）。</summary>
    public string? ApiKey { get; set; }

    /// <summary>API 基础 URL，可选（使用供应商默认端点时留空）。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>界面语言代码，默认 "zh"。</summary>
    public string Language { get; set; } = "zh";
}