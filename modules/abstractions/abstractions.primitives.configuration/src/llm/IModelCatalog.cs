namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型目录接口 - 提供模型列表、别名解析、能力查询
/// </summary>
public interface IModelCatalog {
    /// <summary>
    /// 获取全部已注册供应商目录（供应商身份 + 显示名 + 默认模型 + 模型清单）。
    /// 供应商集合来自配置域注册表（settings.json 的 vendor 节点），UI 不得自行枚举。
    /// </summary>
    IReadOnlyList<ModelProviderCatalog> GetProviders();

    /// <summary>
    /// 构建模型目录快照 — 当前选择 + 全部供应商分组。
    /// 当前供应商的当前模型会补入清单（即使配置里没有），避免 UI 选不中正在生效的模型。
    /// </summary>
    /// <param name="currentProvider">当前供应商标识</param>
    /// <param name="currentModelId">当前模型 ID</param>
    ModelCatalogSnapshot GetSnapshot(string currentProvider, string currentModelId);

    /// <summary>
    /// 获取指定提供商的模型列表
    /// </summary>
    ModelEntry[] GetModelsForProvider(string provider);

    /// <summary>
    /// 按模型 ID 查找上下文窗口大小 — O(1) 字典查找，避免 GetModelsForProvider + FirstOrDefault 的 O(n) 线性检索
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>上下文窗口大小；不存在返回 null</returns>
    int? FindModelContextWindow(string modelId, string provider);

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

    /// <summary>
    /// 检查模型是否支持指定模态能力
    /// </summary>
    bool SupportsModality(string modelId, string provider, ModelModalityKind modality);

    /// <summary>
    /// 获取模型的全部模态能力标志
    /// </summary>
    ModelModalityKind GetModalities(string modelId, string provider);
}