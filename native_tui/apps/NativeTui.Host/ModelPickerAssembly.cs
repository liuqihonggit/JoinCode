namespace NativeTui.Host;

/// <summary>
/// 模型选择器装配（M7-6 全部自研）— 清单从引擎 IModelCatalog 投影（后端获取，青蛙TUI 本地拉取方案已否决）；
/// 切换链自研不经 slash 命令系统（用户决策：不依赖既有命令链防隐藏 bug）：
/// IFastModeService 内存切换 + IConfigurationService 持久化（键 "model" 与 SettingsJson 生成器 jsonName 一致，
/// 语义对齐 ModelCommand.ApplyModelSwitchAsync 同名路径）+ fast mode 自动关（新模型不支持时）。
/// TASK066 D5 effort 通道（唯一数据源 — 接入引擎现有 effort 链，不平行实现）：
/// 清单项能力投影 IModelCatalog.SupportsEffort/SupportsMaxEffort + 会话档位种子
/// IExecutionSettingsProvider.EffortLevel（Auto → null 用焦点默认档）+ 确认应用
/// （执行设置内存写入 + settings.json 持久化键 "effortLevel"，对齐 ModelCommand.PersistPickerEffortAsync
/// 先例；max 不支持保守降级 high，对齐 EffortCommand max 检查）。
/// </summary>
internal static class ModelPickerAssembly
{
    /// <summary>从引擎服务投影选择器清单 — catalog 缺失时单条降级项（不阻断 UI）。
    /// modelHistory 闭环（B类键）：settings.current.modelHistory（LRU 逗号串，最新在前）中的模型
    /// 按历史序置顶（最近使用优先），历史外模型保持目录序；无历史零行为变化。</summary>
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

        var provider = CurrentProvider(services);
        var models = catalog.EnsureCurrentModelInList(catalog.GetModelsForProvider(provider), current);
        var history = LoadModelHistory(services);
        return SortByHistory(models, history)
            .Select(m => new ModelPickerItem(
                m.Id,
                m.DisplayName,
                m.Description,
                m.Id.Equals(current, StringComparison.OrdinalIgnoreCase),
                SupportsEffort: catalog.SupportsEffort(m.Id, provider),
                SupportsMaxEffort: catalog.SupportsMaxEffort(m.Id, provider)))
            .ToArray();
    }

    /// <summary>自研切换链 — 内存主模型 + settings.json 持久化 + fast mode 自关（不支持新模型时）
    /// + modelHistory 记录（头插去重 20 条上限，对齐 ModelCommand.RecordModelHistoryAsync LRU 语义）。</summary>
    internal static void ApplyModel(IServiceProvider services, string modelId)
    {
        var fastMode = services.GetService<IFastModeService>();
        fastMode?.SetPrimaryModel(modelId);
        services.GetService<IConfigurationService>()?.SetAsync("model", modelId).GetAwaiter().GetResult();

        if (fastMode is { IsFastModeActive: true })
        {
            var catalog = services.GetService<IModelCatalog>();
            if (catalog is not null && !catalog.SupportsFastMode(modelId, CurrentProvider(services)))
            {
                fastMode.Deactivate();
            }
        }

        RecordModelHistory(services, modelId);
    }

    /// <summary>读取 modelHistory（逗号串 LRU，最新在前；读取失败/未配置 → 空序 — 零行为变化）。</summary>
    private static List<string> LoadModelHistory(IServiceProvider services)
    {
        try
        {
            var raw = services.GetService<IConfigurationService>()?.GetAsync("modelHistory").GetAwaiter().GetResult();
            return string.IsNullOrEmpty(raw)
                ? []
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return [];
        }
    }

    /// <summary>历史序置顶排序 — 历史中的模型按历史序在前（稳定保序），其余保持目录序。
    /// 模型清单排序纯函数（测试友好），忽略大小写匹配（模型 ID 大小写不敏感惯例）。</summary>
    internal static IEnumerable<ModelEntry> SortByHistory(ModelEntry[] models, List<string> history)
    {
        var historySet = history.ToDictionary(m => m, StringComparer.OrdinalIgnoreCase);
        var inHistory = history
            .Select(h => models.FirstOrDefault(m => string.Equals(m.Id, h, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m is not null)
            .Select(m => m!);
        var notInHistory = models.Where(m => !historySet.ContainsKey(m.Id));
        return inHistory.Concat(notInHistory);
    }

    /// <summary>记录 modelHistory — 头插新模型 + 去重 + 20 条上限（对齐 ModelCommand.RecordModelHistoryAsync；
    /// 写失败静默降级 — 历史记录不阻断模型切换）。</summary>
    private static void RecordModelHistory(IServiceProvider services, string modelId)
    {
        try
        {
            var configService = services.GetService<IConfigurationService>();
            if (configService is null)
            {
                return;
            }

            var history = LoadModelHistory(services);
            history.RemoveAll(m => string.Equals(m, modelId, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, modelId);
            if (history.Count > 20)
            {
                history = history.Take(20).ToList();
            }

            configService.SetAsync("modelHistory", string.Join(",", history)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // 历史写入失败不阻断切换（模型切换已生效）
        }
    }

    /// <summary>当前提供商 — WorkflowConfig DI 单例优先（/vendor 切换后清单实时跟随，TASK058），
    /// 环境变量兜底（对齐 ModelCommand.GetCurrentProvider 同款取值顺序，Host 无 ChatCommandContext）。</summary>
    private static string CurrentProvider(IServiceProvider services) =>
        VendorPickerAssembly.CurrentVendor(services);

    /// <summary>effort 配置投影（TASK066 D5）— 引擎 IExecutionSettingsProvider.EffortLevel：
    /// Low/Medium/High/Max → 对应档位；Auto → null（未设置 — 选择器展示/循环用焦点项默认档，
    /// 基准 effortValue undefined 语义）；引擎无执行设置服务 → null 不启用 effort 区。</summary>
    internal static PickerEffortConfig? LoadEffortConfig(IServiceProvider services)
    {
        var settings = services.GetService<JoinCode.Abstractions.LLM.IExecutionSettingsProvider>();
        return settings is null ? null : new PickerEffortConfig(MapEngineEffort(settings.EffortLevel));
    }

    /// <summary>应用 effort（TASK066 D5 — 选择器确认回调宿主侧）：执行设置内存写入 +
    /// settings.json 持久化（键 "effortLevel"，对齐 ModelCommand.PersistPickerEffortAsync 先例）；
    /// max 档当前主模型不支持时保守降级 high（对齐 EffortCommand max 检查 — 模型已切换完成后调用，
    /// PrimaryModelId 即确认时选中的新模型）。</summary>
    internal static void ApplyEffort(IServiceProvider services, PickerEffortLevel level)
    {
        var applied = level == PickerEffortLevel.Max && !SupportsMaxForCurrentModel(services)
            ? PickerEffortLevel.High
            : level;
        var settings = services.GetService<JoinCode.Abstractions.LLM.IExecutionSettingsProvider>();
        if (settings is not null)
        {
            settings.EffortLevel = MapCoreEffort(applied);
        }

        services.GetService<IConfigurationService>()?.SetAsync("effortLevel", applied.ToLowercaseName()).GetAwaiter().GetResult();
    }

    /// <summary>引擎 EffortLevel → Core 档位（Auto → null）。</summary>
    private static PickerEffortLevel? MapEngineEffort(JoinCode.Abstractions.LLM.EffortLevel level) => level switch
    {
        JoinCode.Abstractions.LLM.EffortLevel.Low => PickerEffortLevel.Low,
        JoinCode.Abstractions.LLM.EffortLevel.Medium => PickerEffortLevel.Medium,
        JoinCode.Abstractions.LLM.EffortLevel.High => PickerEffortLevel.High,
        JoinCode.Abstractions.LLM.EffortLevel.Max => PickerEffortLevel.Max,
        _ => null,
    };

    /// <summary>Core 档位 → 引擎 EffortLevel。</summary>
    private static JoinCode.Abstractions.LLM.EffortLevel MapCoreEffort(PickerEffortLevel level) => level switch
    {
        PickerEffortLevel.Low => JoinCode.Abstractions.LLM.EffortLevel.Low,
        PickerEffortLevel.Medium => JoinCode.Abstractions.LLM.EffortLevel.Medium,
        PickerEffortLevel.High => JoinCode.Abstractions.LLM.EffortLevel.High,
        PickerEffortLevel.Max => JoinCode.Abstractions.LLM.EffortLevel.Max,
        _ => JoinCode.Abstractions.LLM.EffortLevel.High,
    };

    /// <summary>当前主模型是否支持 max 档（目录/主模型缺失时保守 false — 触发降级）。</summary>
    private static bool SupportsMaxForCurrentModel(IServiceProvider services)
    {
        var modelId = services.GetService<IFastModeService>()?.PrimaryModelId;
        var catalog = services.GetService<IModelCatalog>();
        return modelId is not null && catalog is not null && catalog.SupportsMaxEffort(modelId, CurrentProvider(services));
    }
}
