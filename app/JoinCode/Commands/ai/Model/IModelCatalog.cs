namespace JoinCode.ChatCommands;

/// <summary>
/// 模型目录接口 - 提供模型列表、别名解析、能力查询
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// 获取指定提供商的模型列表
    /// </summary>
    ModelEntry[] GetModelsForProvider(string provider);

    /// <summary>
    /// 解析模型别名
    /// </summary>
    string? ResolveAlias(string input, string provider);

    /// <summary>
    /// 获取提供商显示名称
    /// </summary>
    string GetProviderDisplayName(string provider);

    /// <summary>
    /// 获取提供商默认模型
    /// </summary>
    string GetDefaultModelForProvider(string provider);

    /// <summary>
    /// 获取提供商默认快速模型
    /// </summary>
    string GetDefaultFastModelForProvider(string provider);

    /// <summary>
    /// 确保当前模型在列表中
    /// </summary>
    ModelEntry[] EnsureCurrentModelInList(ModelEntry[] models, string currentModelId);

    /// <summary>
    /// 检查模型是否支持 Fast Mode
    /// </summary>
    bool SupportsFastMode(string modelId, string provider);

    /// <summary>
    /// 检查模型是否支持 Effort
    /// </summary>
    bool SupportsEffort(string modelId, string provider);

    /// <summary>
    /// 检查模型是否支持 max Effort
    /// </summary>
    bool SupportsMaxEffort(string modelId, string provider);
}
