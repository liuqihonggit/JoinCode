namespace Core.Prompts.Sections;

/// <summary>
/// 模型信息部分 - 关于当前使用的AI模型
/// </summary>
[PromptSection(Name = "model_info", IsDynamic = true)]
public sealed class ModelInfoSection {
    private readonly IModelConfigLoader _modelConfigLoader;

    /// <summary>
    /// 初始化 <see cref="ModelInfoSection"/> 实例。
    /// </summary>
    /// <param name="modelConfigLoader">模型配置加载器。</param>
    public ModelInfoSection(IModelConfigLoader modelConfigLoader) {
        _modelConfigLoader = modelConfigLoader;
    }

    /// <summary>
    /// 尝试获取模型信息部分的内容。
    /// </summary>
    /// <param name="content">模型信息文本。</param>
    /// <returns>已设置模型 ID 返回 true；否则返回 false。</returns>
    public bool TryGetContent([NotNullWhen(true)] out string? content) {
        var modelId = PromptConfigSnapshot.Current.ModelId;
        var modelName = PromptConfigSnapshot.Current.ModelName;
        if (string.IsNullOrWhiteSpace(modelId)) {
            content = null;
            return false;
        }

        var modelDescription = !string.IsNullOrWhiteSpace(modelName)
            ? $"您由名为 {modelName} 的模型提供支持。确切的模型ID是 {modelId}。"
            : $"您由模型 {modelId} 提供支持。";

        var knowledgeCutoff = GetKnowledgeCutoff(modelId);
        var knowledgeCutoffMessage = !string.IsNullOrWhiteSpace(knowledgeCutoff)
            ? $"\n\n助手知识截止日期是 {knowledgeCutoff}。"
            : "";

        content = $"""
            {modelDescription}{knowledgeCutoffMessage}
            """;
        return true;
    }

    /// <summary>
    /// 创建模型信息提示词部分。
    /// </summary>
    /// <returns>动态系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("model_info", () => new ModelInfoSection(new ModelConfigLoader()).TryGetContent(out var content) ? content : null);

    private string? GetKnowledgeCutoff(string modelId) {
        return _modelConfigLoader.FindModelByModelId(modelId)?.KnowledgeCutoff;
    }
}