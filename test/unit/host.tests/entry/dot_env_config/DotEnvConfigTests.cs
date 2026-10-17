// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace JoinCode.Entry.Tests;

#pragma warning disable JCC3013


[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = ".env 配置测试需直接访问文件系统验证配置加载")]


public sealed class DotEnvConfigTests {
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_OpenAIKey_SetsProviderToOpenAI() {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test123"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("openai");
        config.ApiKey.Should().Be("sk-test123");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AnthropicKey_SetsProviderToAnthropic() {
        var json = """{"env":{"ANTHROPIC_API_KEY":"sk-ant-test"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("anthropic");
        config.ApiKey.Should().Be("sk-ant-test");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AzureKey_SetsProviderToAzure() {
        var json = """{"env":{"AZURE_OPENAI_API_KEY":"azure-key"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("azure");
        config.ApiKey.Should().Be("azure-key");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AgnesKey_SetsProviderToAgnes() {
        var json = """{"env":{"AGNES_API_KEY":"agnes-key"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("agnes");
        config.ApiKey.Should().Be("agnes-key");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AnthropicAuthToken_SetsProviderToAnthropic() {
        var json = """{"env":{"ANTHROPIC_AUTH_TOKEN":"auth-token-123"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("anthropic");
        config.ApiKey.Should().Be("auth-token-123");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_JccVendor_OverridesInferredProvider() {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test","JCC_VENDOR":"anthropic"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Vendor.Should().Be("anthropic");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AzureEndpoint_SetsEndpoint() {
        var json = """{"env":{"AZURE_OPENAI_API_KEY":"azure-key","AZURE_OPENAI_ENDPOINT":"https://my.openai.azure.com"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Endpoint.Should().Be("https://my.openai.azure.com/");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_AnthropicBaseUrl_SetsEndpoint() {
        var json = """{"env":{"ANTHROPIC_API_KEY":"sk-ant","ANTHROPIC_BASE_URL":"https://custom.anthropic.com/v1"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.Endpoint.Should().Be("https://custom.anthropic.com/");
        Cleanup();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void LoadFrom_JccModelId_SetsModelId() {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test","JCC_MODEL_ID":"gpt-4.1"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path, registry: CreateMockRegistry());

        config.Should().NotBeNull();
        config!.ModelId.Should().Be("gpt-4.1");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_NoEnvProperty_ReturnsNull() {
        var json = """{"other":"data"}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().BeNull();
        Cleanup();
    }

    [Fact]
    public void LoadFrom_NonExistentFile_ReturnsNull() {
        var config = DotEnvConfig.LoadFrom(Path.Combine(TempDir, "nonexistent.json"));

        config.Should().BeNull();
        Cleanup();
    }

    [Fact]
    public void LoadFrom_InvalidJson_ReturnsNull() {
        var path = WriteTempFile("not json at all");

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().BeNull();
        Cleanup();
    }

    private string WriteTempFile(string content) {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, $"test_{Guid.NewGuid():N}.json");
        IO.FileSystem.SafeFileIO.WriteAllText(path, content).GetAwaiter().GetResult();
        return path;
    }

    private void Cleanup() {
        try {
            if (Directory.Exists(TempDir)) {
                Directory.Delete(TempDir, true);
            }
        } catch (Exception ex) {
            System.Diagnostics.Trace.WriteLine($"DotEnvConfigTests cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 构建含 openai/anthropic/agnes/azure 定义的 mock registry — 不依赖全局 settings.json
    /// </summary>
    private static IProviderDefinitionRegistry CreateMockRegistry() {
        var providers = new (string Name, string ApiKeyEnvVar, string? EndpointEnvVar)[]
        {
            ("openai", "OPENAI_API_KEY", null),
            ("anthropic", "ANTHROPIC_API_KEY", null),
            ("agnes", "AGNES_API_KEY", null),
            ("azure", "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT"),
        };

        var dict = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, apiKeyEnv, endpointEnv) in providers) {
            var defMock = new Mock<IProviderDefinition>();
            defMock.Setup(d => d.ApiKeyEnvironmentVariable).Returns(apiKeyEnv);
            defMock.Setup(d => d.EndpointEnvironmentVariable).Returns(endpointEnv);
            dict[name] = defMock.Object;
        }

        var registryMock = new Mock<IProviderDefinitionRegistry>();
        registryMock.Setup(r => r.GetRegisteredProviders()).Returns(dict.Keys.ToArray());
        registryMock.Setup(r => r.Contains(It.IsAny<string>())).Returns<string>(name => dict.ContainsKey(name));
        registryMock.Setup(r => r.TryGet(It.IsAny<string>()))
            .Returns<string>(name => dict.GetValueOrDefault(name));
        return registryMock.Object;
    }
}