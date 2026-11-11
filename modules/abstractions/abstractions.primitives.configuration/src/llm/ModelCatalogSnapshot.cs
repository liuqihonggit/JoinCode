namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 供应商目录项 — 供应商身份、显示名、默认模型与模型清单的只读投影。
/// GUI 与 TUI 共用该形状，避免各自从配置文件重建清单。
/// </summary>
/// <param name="Id">供应商标识（settings.json vendor 键）</param>
/// <param name="DisplayName">供应商显示名</param>
/// <param name="DefaultModelId">默认模型 ID（供应商未配置时为空串）</param>
/// <param name="DefaultFastModelId">默认快速模型 ID（供应商未配置时为空串）</param>
/// <param name="Models">模型清单（已应用 availableModels 白名单与环境变量自定义模型）</param>
public sealed record ModelProviderCatalog(
    string Id,
    string DisplayName,
    string DefaultModelId,
    string DefaultFastModelId,
    IReadOnlyList<ModelEntry> Models);

/// <summary>
/// 模型目录快照 — 当前选择 + 全部供应商分组，是 UI 读取模型清单的唯一形状。
/// </summary>
/// <param name="CurrentProvider">当前供应商标识</param>
/// <param name="CurrentModelId">当前模型 ID</param>
/// <param name="Providers">供应商分组（配置顺序）</param>
public sealed record ModelCatalogSnapshot(
    string CurrentProvider,
    string CurrentModelId,
    IReadOnlyList<ModelProviderCatalog> Providers);
