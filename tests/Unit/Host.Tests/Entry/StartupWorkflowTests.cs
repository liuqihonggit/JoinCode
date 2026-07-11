namespace JoinCode.Entry.Tests;

public class StartupWorkflowTests
{
    [Fact]
    public void Azure_CompoundAuthFormat_Should_Parse_Correctly()
    {
        var compoundJson = "{\"apiKey\":\"test-key\",\"endpoint\":\"https://test.openai.azure.com\"}";
        
        var compoundData = System.Text.Json.JsonSerializer.Deserialize(
            compoundJson, 
            ConfigJsonContext.Default.DictionaryStringString);
        
        compoundData.Should().NotBeNull();
        compoundData!["apiKey"].Should().Be("test-key");
        compoundData["endpoint"].Should().Be("https://test.openai.azure.com");
    }

    [Fact]
    public void Azure_MissingEndpoint_Should_FailValidation()
    {
        var compoundJson = "{\"apiKey\":\"test-key\",\"endpoint\":\"\"}";
        
        var compoundData = System.Text.Json.JsonSerializer.Deserialize(
            compoundJson, 
            ConfigJsonContext.Default.DictionaryStringString);
        
        compoundData.Should().NotBeNull();
        var extractedApiKey = compoundData!["apiKey"];
        var extractedEndpoint = compoundData["endpoint"];
        
        var isValid = !string.IsNullOrWhiteSpace(extractedApiKey) && !string.IsNullOrWhiteSpace(extractedEndpoint);
        isValid.Should().BeFalse("缺少 Endpoint 时应该验证失败");
    }

    [Fact]
    public void Azure_MissingApiKey_Should_FailValidation()
    {
        var compoundJson = "{\"apiKey\":\"\",\"endpoint\":\"https://test.openai.azure.com\"}";
        
        var compoundData = System.Text.Json.JsonSerializer.Deserialize(
            compoundJson, 
            ConfigJsonContext.Default.DictionaryStringString);
        
        compoundData.Should().NotBeNull();
        var extractedApiKey = compoundData!["apiKey"];
        var extractedEndpoint = compoundData["endpoint"];
        
        var isValid = !string.IsNullOrWhiteSpace(extractedApiKey) && !string.IsNullOrWhiteSpace(extractedEndpoint);
        isValid.Should().BeFalse("缺少 API Key 时应该验证失败");
    }

    [Fact]
    public void Azure_ValidCompoundFormat_Should_PassValidation()
    {
        var compoundJson = "{\"apiKey\":\"test-key-123\",\"endpoint\":\"https://my-resource.openai.azure.com\"}";
        
        var compoundData = System.Text.Json.JsonSerializer.Deserialize(
            compoundJson, 
            ConfigJsonContext.Default.DictionaryStringString);
        
        compoundData.Should().NotBeNull();
        var extractedApiKey = compoundData!["apiKey"];
        var extractedEndpoint = compoundData["endpoint"];
        
        var isValid = !string.IsNullOrWhiteSpace(extractedApiKey) && !string.IsNullOrWhiteSpace(extractedEndpoint);
        isValid.Should().BeTrue("API Key 和 Endpoint 都存在时应该验证通过");
    }

    [Fact]
    public void Azure_ProviderDefinition_IsValid_Should_RequireBoth()
    {
        var definition = new Core.Configuration.Providers.AzureProviderDefinition();
        
        var configWithBoth = new JoinCode.Abstractions.Configuration.Providers.ProviderConfig
        {
            ApiKey = "test-key",
            Endpoint = "https://test.openai.azure.com"
        };
        definition.IsValid(configWithBoth).Should().BeTrue("API Key 和 Endpoint 都存在");
        
        var configWithOnlyKey = new JoinCode.Abstractions.Configuration.Providers.ProviderConfig
        {
            ApiKey = "test-key",
            Endpoint = ""
        };
        definition.IsValid(configWithOnlyKey).Should().BeFalse("只有 API Key，缺少 Endpoint");
        
        var configWithOnlyEndpoint = new JoinCode.Abstractions.Configuration.Providers.ProviderConfig
        {
            ApiKey = "",
            Endpoint = "https://test.openai.azure.com"
        };
        definition.IsValid(configWithOnlyEndpoint).Should().BeFalse("只有 Endpoint，缺少 API Key");
    }
}
