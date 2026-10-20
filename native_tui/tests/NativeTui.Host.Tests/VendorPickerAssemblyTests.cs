namespace NativeTui.Host.Tests;

/// <summary>
/// 供应商选择器装配测试（TASK058）— 清单投影（VendorKind 枚举唯一数据源 + 显示名/默认模型来自
/// IModelCatalog + 当前供应商标记）+ 切换链（WorkflowConfig.Provider 原地改 + SetPrimaryModel 跟随 +
/// profile 持久化，对齐 GUI SetVendorAsync 语义）。真实引擎 catalog 由 --selftest 覆盖（ADR 0080）。
/// </summary>
public sealed class VendorPickerAssemblyTests
{
    /// <summary>清单投影 — VendorKind 枚举全量 + 显示名走目录 + 当前供应商标记（DI 无 config 时环境兜底）。</summary>
    [Fact]
    public void LoadItems_ProjectsVendorKindWithCurrentMarker()
    {
        using var environment = EnvVarScope.Set(JccEnvVar.Vendor.ToValue(), "zhipu");
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };
        var services = new ServiceCollection().AddSingleton(config).BuildServiceProvider();

        var items = VendorPickerAssembly.LoadItems(services);

        Assert.Equal(Enum.GetValues<VendorKind>().Length, items.Count);
        var zhipu = items.Single(i => i.Id == "zhipu");
        Assert.True(zhipu.IsCurrent);
        Assert.Equal("zhipu", zhipu.DisplayName);
        Assert.False(items.Single(i => i.Id == "openai").IsCurrent);
    }

    /// <summary>切换链 — config.Provider 原地改（Vendor + 默认模型跟随）+ FastMode 主模型同步。</summary>
    [Fact]
    public async Task ApplyVendor_MutatesProviderInPlaceAndFollowsDefaultModel()
    {
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu", ModelId = "glm-4.7" } };
        await using var fastMode = new FastModeService(config);
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IFastModeService>(fastMode)
            .BuildServiceProvider();

        VendorPickerAssembly.ApplyVendor(services, "deepseek");

        Assert.Equal("deepseek", config.Provider.Vendor);
        Assert.Equal(fastMode.PrimaryModelId, config.Provider.ModelId);
    }

    /// <summary>切换链 — profile 持久化（IConfigurationService.SetAsync 收到供应商值）。</summary>
    [Fact]
    public void ApplyVendor_PersistsProfile()
    {
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };
        var configService = new FakeConfigurationService();
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IConfigurationService>(configService)
            .BuildServiceProvider();

        VendorPickerAssembly.ApplyVendor(services, "deepseek");

        Assert.Equal(("profile", "deepseek"), configService.LastSet);
    }

    /// <summary>当前供应商解析 — WorkflowConfig DI 单例优先于环境变量（vendor 切换后实时）。</summary>
    [Fact]
    public void CurrentVendor_PrefersWorkflowConfigOverEnvironment()
    {
        using var environment = EnvVarScope.Set(JccEnvVar.Vendor.ToValue(), "openai");
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };
        var services = new ServiceCollection().AddSingleton(config).BuildServiceProvider();

        Assert.Equal("zhipu", VendorPickerAssembly.CurrentVendor(services));
    }

    /// <summary>记录型配置服务假体（仅捕获 SetAsync 单参重载 — VendorPickerAssembly 消费面；其余成员 throw 超出测试范围）。</summary>
    private sealed class FakeConfigurationService : IConfigurationService
    {
        public (string Key, string Value)? LastSet { get; private set; }

        public event EventHandler<SettingChangeEventArgs>? SettingChanged { add { } remove { } }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetAsync(string key, SettingSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetAsync(string key, string value, CancellationToken cancellationToken = default) {
            LastSet = (key, value);
            return Task.FromResult(true);
        }
        public Task<bool> SetAsync(string key, string value, SettingSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetAsync(string key, string value, SettingSource source, string? appStateKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
