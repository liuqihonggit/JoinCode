namespace NativeTui.Host;

/// <summary>
/// 模型选择器装配（M7-6 全部自研）— 清单从引擎 IModelCatalog 投影（后端获取，青蛙TUI 本地拉取方案已否决）；
/// 切换链自研不经 slash 命令系统（用户决策：不依赖既有命令链防隐藏 bug）：
/// IFastModeService 内存切换 + IConfigurationService 持久化（键 "model" 与 SettingsJson 生成器 jsonName 一致，
/// 语义对齐 ModelCommand.ApplyModelSwitchAsync 同名路径）+ fast mode 自动关（新模型不支持时）。
/// </summary>
internal static class ModelPickerAssembly
{
    /// <summary>从引擎服务投影选择器清单 — catalog 缺失时单条降级项（不阻断 UI）。</summary>
    internal static IReadOnlyList<ModelPickerItem> LoadItems(IServiceProvider services)
    {
        var fastMode = services.GetService<IFastModeService>();
        var current = fastMode?.PrimaryModelId ?? string.Empty;
        var catalog = services.GetService<IModelCatalog>();
        if (catalog is null)
        {
            var fallback = current.Length > 0 ? current : "模型目录不可用";
            return [new ModelPickerItem(fallback, fallback, string.Empty, IsCurrent: true)];
        }

        var provider = CurrentProvider();
        var models = catalog.EnsureCurrentModelInList(catalog.GetModelsForProvider(provider), current);
        return models
            .Select(m => new ModelPickerItem(m.Id, m.DisplayName, m.Description, m.Id.Equals(current, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>自研切换链 — 内存主模型 + settings.json 持久化 + fast mode 自关（不支持新模型时）。</summary>
    internal static void ApplyModel(IServiceProvider services, string modelId)
    {
        var fastMode = services.GetService<IFastModeService>();
        fastMode?.SetPrimaryModel(modelId);
        services.GetService<IConfigurationService>()?.SetAsync("model", modelId).GetAwaiter().GetResult();

        if (fastMode is { IsFastModeActive: true })
        {
            var catalog = services.GetService<IModelCatalog>();
            if (catalog is not null && !catalog.SupportsFastMode(modelId, CurrentProvider()))
            {
                fastMode.Deactivate();
            }
        }
    }

    /// <summary>当前提供商 — 环境变量兜底链（对齐 ModelCommand.GetCurrentProvider 同款取值顺序，Host 无 ChatCommandContext）。</summary>
    private static string CurrentProvider() =>
        Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue()) ?? VendorKind.OpenAi.ToValue();
}
