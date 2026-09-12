namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表远程拉取器 — 从各供应商的 modelsEndpoint 并行拉取最新模型完整元数据
/// </summary>
public interface IModelListFetcher
{
    /// <summary>
    /// 并行拉取所有已配置 modelsEndpoint 的供应商的模型列表
    /// </summary>
    /// <param name="vendor">供应商预设字典（从 settings.json 读取）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>供应商名 → 远程模型信息列表（拉取失败的供应商不包含在结果中）</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<RemoteModelInfo>>> FetchAllAsync(
        IReadOnlyDictionary<string, ProfileSettings> vendor,
        CancellationToken cancellationToken = default);
}
