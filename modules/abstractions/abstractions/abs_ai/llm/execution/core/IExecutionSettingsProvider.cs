namespace JoinCode.Abstractions.LLM;

public interface IExecutionSettingsProvider {
    /// <summary>获取或设置努力级别。</summary>
    EffortLevel EffortLevel { get; set; }
    /// <summary>
    /// 思考模式开关 — 从 settings.json 的 alwaysThinkingEnabled 懈加载,映射到 ChatOptions.ThinkingEnabled
    /// </summary>
    bool ThinkingEnabled { get; set; }
    /// <summary>获取是否启用快速模式。</summary>
    bool FastMode { get; }
    /// <summary>获取快速模型标识。</summary>
    string? FastModelId { get; }
    /// <summary>获取或设置采样温度。</summary>
    float? Temperature { get; set; }
    /// <summary>获取或设置最大 Token 数。</summary>
    int? MaxTokens { get; set; }
}
