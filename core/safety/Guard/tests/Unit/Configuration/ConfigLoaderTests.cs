
namespace Core.Tests.Configuration;

/// <summary>
/// 与 SettingsLoaderTests / ProjectRulesLoaderTests 共享 AppDataConstants 全局状态，需串行执行避免相互污染
/// 测试使用真实 ~/.jcc/auth.json 的 API Key，但隔离 AppData 目录避免读取用户实际配置
/// </summary>
[Collection("AppDataConstantsCollection")]
public class ConfigLoaderTests : IDisposable {
    private static readonly string DefaultOpenAiModelId = ModelConfigLoader.GetDefaultModelId("openai");

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
    private readonly ConfigLoader _loader = new();

    public ConfigLoaderTests() {
        _originalAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarConstants.AppDataFolder);
        _originalProvider = Environment.GetEnvironmentVariable(JccEnvVarConstants.Provider);
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

        // 覆盖用户级环境变量（JCC_PROVIDER 可能存在于用户级环境变量中）
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Provider, ProviderKind.OpenAI.ToValue());
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ModelId, null);
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, _originalAppDataFolder);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Provider, _originalProvider);
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
            $"API Key 应从环境变量加载，但为空。Provider={config.Provider.Provider}");
    }

    [Fact]
    public async Task LoadConfig_ShouldHaveValidProvider()
    {
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.False(string.IsNullOrWhiteSpace(config.Provider.Provider));
        Assert.False(string.IsNullOrWhiteSpace(config.Provider.ModelId));
    }

    [Fact]
    public async Task LoadConfig_ShouldHaveDefaultValues() {
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, null);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.Equal(DefaultOpenAiModelId, config.Provider.ModelId);
        Assert.Equal("workflow_state.json", config.StateFilePath);
        Assert.NotNull(config.Bridge);
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
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Provider, "anthropic");
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ModelId, "claude-3-opus");
        // 清除 Provider 专属环境变量，让 JCC_API_KEY 生效
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.AnthropicApiKey, null);
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.AgnesApiKey, null);
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, null);
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, realKey);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        Assert.Equal("anthropic", config.Provider.Provider);
        Assert.Equal("claude-3-opus", config.Provider.ModelId);
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
    public async Task LoadConfig_ProviderEnvOverridesJccApiKey()
    {
        // Provider 专属环境变量优先级高于 JCC_API_KEY
        var realKey = TestConfiguration.GetRealApiKey();
        Environment.SetEnvironmentVariable(JccEnvVarConstants.ApiKey, "jcc-key-should-be-overridden");
        Environment.SetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey, realKey);

        var config = await _loader.LoadConfigAsync(_fs).ConfigureAwait(true);

        // Provider 专属环境变量应覆盖 JCC_API_KEY
        Assert.Equal(realKey, config.Provider.ApiKey);
    }
}
