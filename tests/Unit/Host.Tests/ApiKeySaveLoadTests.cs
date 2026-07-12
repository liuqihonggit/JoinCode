namespace JoinCode.Host.Tests;

using Core.Configuration;
using Core.Configuration.Providers;
using JoinCode.Abstractions.Exceptions;

public class ApiKeySaveLoadTests
{
    private static ConfigLoader Loader => new();

    [Fact]
    public async Task SaveApiKey_AndLoad_ShouldUpdateProviderConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var originalPaths = AppDataConstants.Paths;
        var fs = new PhysicalFileSystem();
        
        try
        {
            AppDataConstants.Paths = AppDataPaths.FromEnvironment() with
            {
                AppDataFolder = tempDir
            };
            
            var provider = "agnes";
            var apiKey = "test-api-key-12345";
            
            await ConfigLoader.SaveApiKeyToJccAsync(provider, apiKey, fs).ConfigureAwait(true);
            
            var authPath = WorkflowConstants.Paths.AuthFilePath;
            fs.FileExists(authPath).Should().BeTrue($"auth.json should exist at {authPath}");
            
            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs).ConfigureAwait(true);
            loadedKey.Should().Be(apiKey, "Loaded API key should match saved key");
            
            WorkflowConfig config;
            try
            {
                config = await Loader.LoadConfigAsync(fs).ConfigureAwait(true);
            }
            catch (ConfigurationException)
            {
                config = new WorkflowConfig();
            }
            
            config.Provider.Provider = provider;
            config.Provider.ApiKey = apiKey;
            
            var definition = ProviderDefinitionRegistry.TryGetStatic(provider);
            if (definition is not null)
            {
                config.Provider.Definition = definition;
                config.Provider.ModelId = definition.DefaultModelId;
            }
            
            config.Provider.Provider.Should().Be(provider);
            config.Provider.ApiKey.Should().Be(apiKey);
            config.Provider.Definition.Should().NotBeNull();
        }
        finally
        {
            AppDataConstants.Paths = originalPaths;
            if (fs.DirectoryExists(tempDir))
            {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }
    
    [Fact]
    public async Task SaveApiKey_WithDifferentProvider_ShouldLoadCorrectProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var originalPaths = AppDataConstants.Paths;
        var fs = new PhysicalFileSystem();
        
        try
        {
            AppDataConstants.Paths = AppDataPaths.FromEnvironment() with
            {
                AppDataFolder = tempDir
            };
            
            await ConfigLoader.SaveApiKeyToJccAsync("openai", "openai-key", fs).ConfigureAwait(true);
            await ConfigLoader.SaveApiKeyToJccAsync("anthropic", "anthropic-key", fs).ConfigureAwait(true);
            
            var openaiKey = await Loader.LoadApiKeyFromJccAsync("openai", fs).ConfigureAwait(true);
            var anthropicKey = await Loader.LoadApiKeyFromJccAsync("anthropic", fs).ConfigureAwait(true);
            
            openaiKey.Should().Be("openai-key");
            anthropicKey.Should().Be("anthropic-key");
        }
        finally
        {
            AppDataConstants.Paths = originalPaths;
            if (fs.DirectoryExists(tempDir))
            {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }
    
    [Fact]
    public async Task SaveApiKey_OverwriteExisting_ShouldUpdateValue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var originalPaths = AppDataConstants.Paths;
        var fs = new PhysicalFileSystem();
        
        try
        {
            AppDataConstants.Paths = AppDataPaths.FromEnvironment() with
            {
                AppDataFolder = tempDir
            };
            
            var provider = "agnes";
            
            await ConfigLoader.SaveApiKeyToJccAsync(provider, "old-key", fs).ConfigureAwait(true);
            
            var authPath = WorkflowConstants.Paths.AuthFilePath;
            var json1 = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json1.Should().Contain("old-key");
            
            await ConfigLoader.SaveApiKeyToJccAsync(provider, "new-key-123", fs).ConfigureAwait(true);
            
            var json2 = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json2.Should().Contain("new-key-123");
            json2.Should().NotContain("old-key");
            
            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs).ConfigureAwait(true);
            loadedKey.Should().Be("new-key-123", "Should load the new key after overwrite");
        }
        finally
        {
            AppDataConstants.Paths = originalPaths;
            if (fs.DirectoryExists(tempDir))
            {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }
    
    [Theory]
    [InlineData("openai", "sk-openai-test-123")]
    [InlineData("anthropic", "sk-ant-test-456")]
    [InlineData("azure", "azure-key-test-789")]
    [InlineData("agnes", "agnes-key-test-abc")]
    public async Task SaveApiKey_ForEachProvider_ShouldSaveAndLoadCorrectly(string provider, string apiKey)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        var originalPaths = AppDataConstants.Paths;
        var fs = new PhysicalFileSystem();
        
        try
        {
            AppDataConstants.Paths = AppDataPaths.FromEnvironment() with
            {
                AppDataFolder = tempDir
            };
            
            await ConfigLoader.SaveApiKeyToJccAsync(provider, apiKey, fs).ConfigureAwait(true);
            
            var authPath = WorkflowConstants.Paths.AuthFilePath;
            fs.FileExists(authPath).Should().BeTrue();
            
            var json = await fs.ReadAllTextAsync(authPath).ConfigureAwait(true);
            json.Should().Contain(provider);
            json.Should().Contain(apiKey);
            
            var loadedKey = await Loader.LoadApiKeyFromJccAsync(provider, fs).ConfigureAwait(true);
            loadedKey.Should().Be(apiKey, $"Loaded key for {provider} should match");
            
            WorkflowConfig config;
            try
            {
                config = await Loader.LoadConfigAsync(fs).ConfigureAwait(true);
            }
            catch (ConfigurationException)
            {
                config = new WorkflowConfig();
            }
            
            config.Provider.Provider = provider;
            config.Provider.ApiKey = apiKey;
            
            var definition = ProviderDefinitionRegistry.TryGetStatic(provider);
            if (definition is not null)
            {
                config.Provider.Definition = definition;
                config.Provider.ModelId = definition.DefaultModelId;
            }
            
            config.Provider.Provider.Should().Be(provider);
            config.Provider.ApiKey.Should().Be(apiKey);
        }
        finally
        {
            AppDataConstants.Paths = originalPaths;
            if (fs.DirectoryExists(tempDir))
            {
                fs.DeleteDirectory(tempDir, recursive: true);
            }
        }
    }
}
