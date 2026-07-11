namespace Core.Prompts.Sections;

/// <summary>
/// 模型信息部分 - 关于当前使用的AI模型
/// </summary>
[PromptSection(Name = "model_info", Order = 67, IsDynamic = true)]
public static class ModelInfoSection {
    public static string? GetContent() {
        var modelId = PromptConfigSnapshot.Current.ModelId;
        var modelName = PromptConfigSnapshot.Current.ModelName;
        if (string.IsNullOrWhiteSpace(modelId)) {
            return null;
        }

        var modelDescription = !string.IsNullOrWhiteSpace(modelName)
            ? $"您由名为 {modelName} 的模型提供支持。确切的模型ID是 {modelId}。"
            : $"您由模型 {modelId} 提供支持。";

        var knowledgeCutoff = GetKnowledgeCutoff(modelId);
        var knowledgeCutoffMessage = !string.IsNullOrWhiteSpace(knowledgeCutoff)
            ? $"\n\n助手知识截止日期是 {knowledgeCutoff}。"
            : "";

        return $"""
            {modelDescription}{knowledgeCutoffMessage}
            """;
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("model_info", GetContent);

    private static string? GetKnowledgeCutoff(string modelId) {
        // 根据模型ID返回知识截止日期
        var canonical = modelId.ToLowerInvariant();

        return canonical switch {
            var s when s.Contains("gpt-4") => "2024年4月",
            var s when s.Contains("gpt-3.5") => "2021年9月",
            var s when s.Contains("claude-3-opus") => "2024年2月",
            var s when s.Contains("claude-3-sonnet") => "2024年2月",
            var s when s.Contains("claude-3-haiku") => "2023年8月",
            _ => null
        };
    }
}
