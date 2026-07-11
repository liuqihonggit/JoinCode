namespace JoinCode.Entry.Tests;

#pragma warning disable JCC9001, JCC3013

public sealed class DotEnvConfigTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");

    [Fact]
    public void LoadFrom_OpenAIKey_SetsProviderToOpenAI()
    {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test123"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("openai");
        config.ApiKey.Should().Be("sk-test123");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AnthropicKey_SetsProviderToAnthropic()
    {
        var json = """{"env":{"ANTHROPIC_API_KEY":"sk-ant-test"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("anthropic");
        config.ApiKey.Should().Be("sk-ant-test");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AzureKey_SetsProviderToAzure()
    {
        var json = """{"env":{"AZURE_OPENAI_API_KEY":"azure-key"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("azure");
        config.ApiKey.Should().Be("azure-key");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AgnesKey_SetsProviderToAgnes()
    {
        var json = """{"env":{"AGNES_API_KEY":"agnes-key"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("agnes");
        config.ApiKey.Should().Be("agnes-key");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AnthropicAuthToken_SetsProviderToAnthropic()
    {
        var json = """{"env":{"ANTHROPIC_AUTH_TOKEN":"auth-token-123"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("anthropic");
        config.ApiKey.Should().Be("auth-token-123");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_JccProvider_OverridesInferredProvider()
    {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test","JCC_PROVIDER":"anthropic"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Provider.Should().Be("anthropic");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_JccApiKey_SetsApiKeyWithoutProvider()
    {
        var json = """{"env":{"JCC_API_KEY":"jcc-key"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.ApiKey.Should().Be("jcc-key");
        config!.Provider.Should().BeNull();
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AzureEndpoint_SetsEndpoint()
    {
        var json = """{"env":{"AZURE_OPENAI_API_KEY":"azure-key","AZURE_OPENAI_ENDPOINT":"https://my.openai.azure.com"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Endpoint.Should().Be("https://my.openai.azure.com/");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_AnthropicBaseUrl_SetsEndpoint()
    {
        var json = """{"env":{"ANTHROPIC_API_KEY":"sk-ant","ANTHROPIC_BASE_URL":"https://custom.anthropic.com/v1"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.Endpoint.Should().Be("https://custom.anthropic.com/");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_JccModelId_SetsModelId()
    {
        var json = """{"env":{"OPENAI_API_KEY":"sk-test","JCC_MODEL_ID":"gpt-4.1"}}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().NotBeNull();
        config!.ModelId.Should().Be("gpt-4.1");
        Cleanup();
    }

    [Fact]
    public void LoadFrom_NoEnvProperty_ReturnsNull()
    {
        var json = """{"other":"data"}""";
        var path = WriteTempFile(json);

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().BeNull();
        Cleanup();
    }

    [Fact]
    public void LoadFrom_NonExistentFile_ReturnsNull()
    {
        var config = DotEnvConfig.LoadFrom(Path.Combine(TempDir, "nonexistent.json"));

        config.Should().BeNull();
        Cleanup();
    }

    [Fact]
    public void LoadFrom_InvalidJson_ReturnsNull()
    {
        var path = WriteTempFile("not json at all");

        var config = DotEnvConfig.LoadFrom(path);

        config.Should().BeNull();
        Cleanup();
    }

    private string WriteTempFile(string content)
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, $"test_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"DotEnvConfigTests cleanup failed: {ex.Message}");
        }
    }
}
