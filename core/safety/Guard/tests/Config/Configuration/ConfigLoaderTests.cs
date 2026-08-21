
namespace Core.Tests.Configuration;

/// <summary>
/// 与 SettingsLoaderTests / ProjectRulesLoaderTests 共享 AppDataConstants 全局状态，需串行执行避免相互污染
/// 测试使用真实 ~/.jcc/auth.json 的 API Key，但隔离 AppData 目录避免读取用户实际配置
/// </summary>
[Collection("AppDataConstantsCollection")]
public class ConfigLoaderTests : IDisposable {
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string DefaultOpenAiModelId = Loader.GetDefaultModelId("openai");

    private readonly string? _originalAppDataFolder;
    private readonly string? _originalProvider;
    private readonly string? _originalModelId;
    private readonly string? _originalApiKey;
    private readonly string? _originalAgnesApiKey;
    private readonly string? _originalOpenAiApiKey;
    private readonly string? _originalCodeExecutionTimeout;
    private readonly string? _originalCodeExecutionMaxMemory;
    private readonly string _tempAppDataDir;
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly ConfigLoader _loader;

    public ConfigLoaderTests() {
        _originalAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarConstants.AppDataFolder);
        _originalProvider = Environment.GetEnvironmentVariable(JccEnvVarConstants.Vendor);
        _originalModelId = Environment.GetEnvironmentVariable(JccEnvVarConstants.ModelId);
        _originalApiKey = Environment.GetEnvironmentVariable(JccEnvVarConstants.ApiKey);
        _originalAgnesApiKey = Environment.GetEnvironmentVariable(ProviderEnvVarConstants.AgnesApiKey);
        _originalOpenAiApiKey = Environment.GetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey);
        _originalCodeExecutionTimeout = Environment.GetEnvironmentVariable(JccEnvVarConstants.CodeExecutionTimeout);
        _originalCodeExecutionMaxMemory = Environment.GetEnvironmentVariable(JccEnvVarConstants.CodeExecutionMaxMemory);

        // 隔离: 使用临时目录避免读取用户实际的 ~/.jcc/settings.json
        _tempAppDataDir = $"/test/jcc-test-config-{Guid.NewGuid():N}";
        _fs.CreateDirectory(_tempAppDataDir);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, _tempAppDataDir);

        // 刷新 AppDataConstants.Paths 以反映新的环境变量
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();

        // 在临时目录写入 settings.json，提供 vendor 和 model 配置
        var settingsJson = """{"vendor":{"openai":{"protocol":"openai-compatible","apiKeyEnvVar":"OPENAI_API_KEY","model":"gpt-4o","models":[{"id":"gpt-4o","displayName":"GPT-4o","contextWindow":128000,"aliases":["4o","default"],"capabilities":{"fastMode":true,"modalities":["text","readImage","readPdf","toolUse"]}},{"id":"gpt-4o-mini","displayName":"GPT-4o Mini","contextWindow":128000,"aliases":["mini","fast"],"capabilities":{"fastMode":true,"modalities":["text","readImage","readPdf","toolUse"]}}]},"anthropic":{"protocol":"anthropic","apiKeyEnvVar":"ANTHROPIC_API_KEY","model":"claude-opus-4-7-20250701","models":[{"id":"claude-opus-4-7-20250701","displayName":"Claude Opus 4.7","contextWindow":1000000,"aliases":["opus"],"capabilities":{"thinkingMode":true,"modalities":["text","readImage","readPdf","thinking","toolUse"]}},{"id":"claude-sonnet-4-6-20250514","displayName":"Claude Sonnet 4.6","contextWindow":1000000,"aliases":["sonnet"],"capabilities":{"fastMode":true,"modalities":["text","readImage","readPdf","thinking","toolUse"]}}]}},"current":{"vendor":"openai","model":"gpt-4o"}}""";
        _fs.WriteAllText(AppDataConstants.Paths.SettingsFilePath, settingsJson);

        // 覆盖用户级环境变量（JCC_VENDOR 可能存在于用户级环境变量中）
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Vendor, VendorKind.OpenAi.ToValue());
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ModelId, null);

        // 在临时目录和 settings.json 准备好之后创建 ConfigLoader，
        // 传入自定义 ProviderDefinitionRegistry，确保 CI 环境也能找到 openai/anthropic 等 Provider
        _loader = new ConfigLoader(registry: new TestProviderDefinitionRegistry());
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, _originalAppDataFolder);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Vendor, _originalProvider);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ModelId, _originalModelId);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, _originalApiKey);
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.AgnesApiKey, _originalAgnesApiKey);
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, _originalOpenAiApiKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.CodeExecutionTimeout, _originalCodeExecutionTimeout);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.CodeExecutionMaxMemory, _originalCodeExecutionMaxMemory);

        // 恢复 AppDataConstants.Paths
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadConfig_WithRealApiKeyFromEnv_ShouldHaveApiKey()
    {
        // 使用真实 API Key（从环境变量或 ~/.jcc/auth.json 读取）
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.False(string.IsNullOrWhiteSpace(config.Provider.ApiKey),
            $"API Key 应从环境变量加载，但为空。Provider={config.Provider.Vendor}");
    }

    [Fact]
    public async Task LoadConfig_ShouldHaveValidProvider()
    {
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.False(string.IsNullOrWhiteSpace(config.Provider.Vendor));
        Assert.False(string.IsNullOrWhiteSpace(config.Provider.ModelId));
    }

    [Fact]
    public async Task LoadConfig_ShouldHaveDefaultCodeExecutionConfig() {
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.NotNull(config.CodeExecution);
        Assert.Equal(10, config.CodeExecution.ExecutionTimeoutSeconds);
        Assert.Equal(100, config.CodeExecution.MaxMemoryMB);
        Assert.False(config.CodeExecution.AllowNetworkAccess);
    }

    [Fact]
    public async Task LoadConfig_ShouldHaveDefaultBridgeConfig()
    {
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.NotNull(config.Bridge);
    }

    [Fact]
    public async Task LoadConfig_JccEnvVarsOverrideDefaults()
    {
        // 设置环境变量覆盖 Provider 和 ModelId
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Vendor, "anthropic");
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ModelId, "claude-opus-4-7-20250701");
        // 清除其他 Provider 专属环境变量，让 ANTHROPIC_API_KEY 生效
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.AgnesApiKey, null);
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, null);
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.AnthropicApiKey, realKey);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.Equal("anthropic", config.Provider.Vendor);
        Assert.Equal("claude-opus-4-7-20250701", config.Provider.ModelId);
        Assert.Equal(realKey, config.Provider.ApiKey);
    }

    [Fact]
    public async Task LoadConfig_CodeExecutionEnvVars()
    {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.CodeExecutionTimeout, "60");
        Environment.SetEnvironmentVariable(JccEnvVarConstants.CodeExecutionMaxMemory, "512");
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.Equal(60, config.CodeExecution.ExecutionTimeoutSeconds);
        Assert.Equal(512, config.CodeExecution.MaxMemoryMB);
    }

    [Fact]
    public async Task LoadConfig_ProviderEnvKeyUsed()
    {
        // Provider 专属环境变量提供 API Key
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.Equal(realKey, config.Provider.ApiKey);
    }

    /// <summary>
    /// 测试专用 Provider 注册表 — 不依赖全局 settings.json，注册所有测试需要的 Provider
    /// </summary>
    private sealed class TestProviderDefinitionRegistry : IProviderDefinitionRegistry
    {
        private readonly Dictionary<string, IProviderDefinition> _definitions;

        public TestProviderDefinitionRegistry()
        {
            var loader = new ModelConfigLoader();
            _definitions = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new OpenAiCompatibleProviderDefinition(loader, "openai", "OPENAI_API_KEY"),
                ["anthropic"] = new AnthropicCompatibleProviderDefinition(loader, "anthropic", "ANTHROPIC_API_KEY"),
                ["deepseek"] = new OpenAiCompatibleProviderDefinition(loader, "deepseek", "DEEPSEEK_API_KEY"),
                ["azure"] = new AzureProviderDefinition(loader),
            };
        }

        public IProviderDefinition? TryGet(string providerName) => _definitions.GetValueOrDefault(providerName);
        public IReadOnlyCollection<string> RegisteredProviders => _definitions.Keys;
    }
}
