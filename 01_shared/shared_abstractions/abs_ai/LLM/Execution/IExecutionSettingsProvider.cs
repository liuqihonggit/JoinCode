namespace JoinCode.Abstractions.LLM;

public interface IExecutionSettingsProvider
{
    EffortLevel EffortLevel { get; set; }
    /// <summary>
    /// 思考模式开关 — 从 settings.json 的 alwaysThinkingEnabled 懒加载,映射到 ChatOptions.ThinkingEnabled
    /// </summary>
    bool ThinkingEnabled { get; set; }
    bool FastMode { get; }
    string? FastModelId { get; }
    float? Temperature { get; set; }
    int? MaxTokens { get; set; }
}
