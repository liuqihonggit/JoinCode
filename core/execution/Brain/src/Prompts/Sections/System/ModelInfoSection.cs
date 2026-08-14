namespace Core.Prompts.Sections;

/// <summary>
/// 模型信息部分 - 关于当前使用的AI模型
/// </summary>
[PromptSection(Name = "model_info", Order = 67, IsDynamic = true)]
public sealed class ModelInfoSection
{
    private readonly IModelConfigLoader _modelConfigLoader;

    public ModelInfoSection(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    public string? GetContent()
    {
        var modelId = PromptConfigSnapshot.Current.ModelId;
        var modelName = PromptConfigSnapshot.Current.ModelName;
        if (string.IsNullOrWhiteSpace(modelId))
        {
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
        SystemPromptSection.Dynamic("model_info", () => new ModelInfoSection(new ModelConfigLoader()).GetContent());

    private string? GetKnowledgeCutoff(string modelId)
    {
        return _modelConfigLoader.FindModelByModelId(modelId)?.KnowledgeCutoff;
    }
}
