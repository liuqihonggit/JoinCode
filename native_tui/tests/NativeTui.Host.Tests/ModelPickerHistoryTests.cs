namespace NativeTui.Host.Tests;

/// <summary>
/// 模型选择器 modelHistory 接线测试（B类键闭环）— settings.current.modelHistory（LRU 逗号串，
/// 最新在前）消费方：清单按最近使用置顶排序（其余保持目录序）+ ApplyModel 记录历史
///（对齐 ModelCommand.RecordModelHistoryAsync 同款 LRU 去重 20 条上限语义）。
/// 真实引擎链路由 --selftest 覆盖（ADR 0080）。
/// </summary>
public sealed class ModelPickerHistoryTests
{
    /// <summary>LoadItems 排序 — modelHistory 中的模型按历史序置顶，其余保持目录序。</summary>
    [Fact]
    public void LoadItems_SortsByModelHistory_RecentFirst()
    {
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IFastModeService>(new FastModeService(config))
            .AddSingleton<IModelCatalog>(new FakeModelCatalog())
            .AddSingleton<IConfigurationService>(new FakeHistoryConfigService("glm-4-flash,glm-4.7"))
            .BuildServiceProvider();

        var items = ModelPickerAssembly.LoadItems(services);

        // 历史中的两个模型置顶且保持历史序（最新 glm-4-flash 在前），目录中其余模型跟随
        Assert.Equal("glm-4-flash", items[0].Id);
        Assert.Equal("glm-4.7", items[1].Id);
        Assert.Equal("glm-4-plus", items[2].Id);
    }

    /// <summary>LoadItems 无历史 — 清单保持目录序（零行为变化）。</summary>
    [Fact]
    public void LoadItems_NoHistory_KeepsCatalogOrder()
    {
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IFastModeService>(new FastModeService(config))
            .AddSingleton<IModelCatalog>(new FakeModelCatalog())
            .AddSingleton<IConfigurationService>(new FakeHistoryConfigService(null))
            .BuildServiceProvider();

        var items = ModelPickerAssembly.LoadItems(services);

        Assert.Equal("glm-4.7", items[0].Id);
        Assert.Equal("glm-4-plus", items[1].Id);
        Assert.Equal("glm-4-flash", items[2].Id);
    }

    /// <summary>ApplyModel 记录历史 — 新模型头插 + 去重（对齐 ModelCommand.RecordModelHistoryAsync）。</summary>
    [Fact]
    public async Task ApplyModel_RecordsHistory_LruDedupHeadInsert()
    {
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu", ModelId = "glm-4.7" } };
        await using var fastMode = new FastModeService(config);
        var fake = new FakeHistoryConfigService("glm-4.7,glm-4-flash");
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IFastModeService>(fastMode)
            .AddSingleton<IConfigurationService>(fake)
            .BuildServiceProvider();

        ModelPickerAssembly.ApplyModel(services, "glm-4-plus");

        // 头插新模型 + 去重已有 + 逗号串（LRU 语义，最新在前）
        Assert.Equal(("modelHistory", "glm-4-plus,glm-4.7,glm-4-flash"), fake.LastSet);
    }

    /// <summary>可读+可写的记录型配置假体（modelHistory 键读写 — 本测试消费面；其余成员 throw）。</summary>
    private sealed class FakeHistoryConfigService : IConfigurationService
    {
        private readonly string? _modelHistory;
        public (string Key, string Value)? LastSet { get; private set; }

        public FakeHistoryConfigService(string? modelHistory) => _modelHistory = modelHistory;

        public event EventHandler<SettingChangeEventArgs>? SettingChanged { add { } remove { } }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            key == "modelHistory" ? Task.FromResult(_modelHistory) : Task.FromResult<string?>(null);
        public Task<string?> GetAsync(string key, SettingSource source, CancellationToken cancellationToken = default) => GetAsync(key, cancellationToken);
        public Task<bool> SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            LastSet = (key, value);
            return Task.FromResult(true);
        }
        public Task<bool> SetAsync(string key, string value, SettingSource source, CancellationToken cancellationToken = default) => SetAsync(key, value, cancellationToken);
        public Task<bool> SetAsync(string key, string value, SettingSource source, string? appStateKey, CancellationToken cancellationToken = default) => SetAsync(key, value, cancellationToken);
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>固定三模型目录假体（目录序 glm-4.7 → glm-4-plus → glm-4-flash）。</summary>
    private sealed class FakeModelCatalog : IModelCatalog
    {
        private static readonly ModelEntry[] Models =
        [
            new("glm-4.7", "GLM 4.7", 128000),
            new("glm-4-plus", "GLM 4 Plus", 128000),
            new("glm-4-flash", "GLM 4 Flash", 128000),
        ];

        public ModelEntry[] GetModelsForProvider(string provider) => Models;
        public IReadOnlyList<ModelProviderCatalog> GetProviders() => [];
        public ModelCatalogSnapshot GetSnapshot(string currentProvider, string currentModelId) =>
            new(currentProvider, currentModelId, []);
        public int? FindModelContextWindow(string modelId, string provider) => null;
        public string? ResolveAlias(string input, string provider) => null;
        public string GetProviderDisplayName(string provider) => provider;
        public string GetDefaultModelForProvider(string provider) => "glm-4.7";
        public string GetDefaultFastModelForProvider(string provider) => "glm-4-flash";
        public ModelEntry[] EnsureCurrentModelInList(ModelEntry[] models, string currentModelId) => models;
        public bool SupportsFastMode(string modelId, string provider) => true;
        public bool SupportsEffort(string modelId, string provider) => true;
        public bool SupportsMaxEffort(string modelId, string provider) => false;
        public bool SupportsModality(string modelId, string provider, ModelModalityKind modality) => true;
        public ModelModalityKind GetModalities(string modelId, string provider) => ModelModalityKind.Text;
    }
}
