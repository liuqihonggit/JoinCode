namespace JoinCode.Host.Tests;


public class ApiKeySaveLoadTests {
    private static ConfigLoader Loader => new();
    private static readonly IModelConfigLoader ModelLoader = new JoinCode.Abstractions.Configuration.Llm.ModelConfigLoader();

    private static IProviderDefinition GetDefinitionFor(string provider) {
        if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
            return new AzureProviderDefinition(ModelLoader);
        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            return new AnthropicCompatibleProviderDefinition(ModelLoader, provider, "ANTHROPIC_API_KEY");
        return new OpenAiCompatibleProviderDefinition(ModelLoader, provider,
            provider.ToLowerInvariant() switch {
                "openai" => "OPENAI_API_KEY",
                "deepseek" => "DEEPSEEK_API_KEY",
                "agnes" => "AGNES_API_KEY",
                _ => null
            });
    }

    [Fact]
    public async Task SaveApiKey_AndLoad_ShouldUpdateProviderConfig() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var paths = AppDataPaths.FromEnvironment() with {
            AppDataFolder = tempDir
        };
        await using var fs = new PhysicalFileSystem();

        try {
            var provider = "agnes";
            var apiKey = "test-api-key-12345";

            await ConfigLoader.SaveApiKeyToJccAsync(provider, apiKey, fs, paths: paths).ConfigureAwait(true);

            var authPath = paths.AuthFilePath;
            fs.FileExists(authPath).Should().BeTrue($"auth.json should exist at {authPath}");

            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs, paths: paths).ConfigureAwait(true);
            loadedKey.Should().Be(apiKey, "Loaded API key should match saved key");

            WorkflowConfig config;
            try {
                config = await Loader.LoadConfigAsync(fs).ConfigureAwait(true);
            } catch (ConfigurationException) {
                config = new WorkflowConfig();
            }

            config.Provider.Vendor = provider;
            config.Provider.ApiKey = apiKey;

            var definition = GetDefinitionFor(provider);
            if (definition is not null) {
                config.Provider.Definition = definition;
                config.Provider.ModelId = definition.DefaultModelId;
            }

            config.Provider.Vendor.Should().Be(provider);
            config.Provider.ApiKey.Should().Be(apiKey);
            config.Provider.Definition.Should().NotBeNull();
        } finally {
            if (fs.DirectoryExists(tempDir)) {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveApiKey_WithDifferentProvider_ShouldLoadCorrectProvider() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var paths = AppDataPaths.FromEnvironment() with {
            AppDataFolder = tempDir
        };
        await using var fs = new PhysicalFileSystem();

        try {
            await ConfigLoader.SaveApiKeyToJccAsync("openai", "openai-key", fs, paths: paths).ConfigureAwait(true);
            await ConfigLoader.SaveApiKeyToJccAsync("anthropic", "anthropic-key", fs, paths: paths).ConfigureAwait(true);

            var openaiKey = await Loader.LoadApiKeyFromJccAsync("openai", fs, paths: paths).ConfigureAwait(true);
            var anthropicKey = await Loader.LoadApiKeyFromJccAsync("anthropic", fs, paths: paths).ConfigureAwait(true);

            openaiKey.Should().Be("openai-key");
            anthropicKey.Should().Be("anthropic-key");
        } finally {
            if (fs.DirectoryExists(tempDir)) {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveApiKey_OverwriteExisting_ShouldUpdateValue() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var paths = AppDataPaths.FromEnvironment() with {
            AppDataFolder = tempDir
        };
        await using var fs = new PhysicalFileSystem();

        try {
            var provider = "agnes";

            await ConfigLoader.SaveApiKeyToJccAsync(provider, "old-key", fs, paths: paths).ConfigureAwait(true);

            var authPath = paths.AuthFilePath;
            var json1 = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json1.Should().Contain("old-key");

            await ConfigLoader.SaveApiKeyToJccAsync(provider, "new-key-123", fs, paths: paths).ConfigureAwait(true);

            var json2 = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json2.Should().Contain("new-key-123");
            json2.Should().NotContain("old-key");

            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs, paths: paths).ConfigureAwait(true);
            loadedKey.Should().Be("new-key-123", "Should load the new key after overwrite");
        } finally {
            if (fs.DirectoryExists(tempDir)) {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("openai", "sk-openai-test-123")]
    [InlineData("anthropic", "sk-ant-test-456")]
    [InlineData("azure", "azure-key-test-789")]
    [InlineData("agnes", "agnes-key-test-abc")]
    public async Task SaveApiKey_ForEachProvider_ShouldSaveAndLoadCorrectly(string provider, string apiKey) {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var paths = AppDataPaths.FromEnvironment() with {
            AppDataFolder = tempDir
        };
        await using var fs = new PhysicalFileSystem();

        try {
            await ConfigLoader.SaveApiKeyToJccAsync(provider, apiKey, fs, paths: paths).ConfigureAwait(true);

            var authPath = paths.AuthFilePath;
            fs.FileExists(authPath).Should().BeTrue();

            var json = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json.Should().Contain(provider);
            json.Should().Contain(apiKey);

            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs, paths: paths).ConfigureAwait(true);
            loadedKey.Should().Be(apiKey, $"Loaded key for {provider} should match");

            WorkflowConfig config;
            try {
                config = await Loader.LoadConfigAsync(fs).ConfigureAwait(true);
            } catch (ConfigurationException) {
                config = new WorkflowConfig();
            }

            config.Provider.Vendor = provider;
            config.Provider.ApiKey = apiKey;

            var definition = GetDefinitionFor(provider);
            if (definition is not null) {
                config.Provider.Definition = definition;
                config.Provider.ModelId = definition.DefaultModelId;
            }

            config.Provider.Vendor.Should().Be(provider);
            config.Provider.ApiKey.Should().Be(apiKey);
        } finally {
            if (fs.DirectoryExists(tempDir)) {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }
}
