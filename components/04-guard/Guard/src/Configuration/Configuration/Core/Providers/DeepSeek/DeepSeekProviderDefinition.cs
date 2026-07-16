
namespace Core.Configuration.Providers;

public sealed class DeepSeekProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    protected override string ProviderConfigKey => "deepseek";
    protected override string DefaultBaseUrl => "https://api.deepseek.com/";

    public override ProviderKind Kind => ProviderKind.DeepSeek;
    public override string ProviderName => ProviderKind.DeepSeek.ToValue();
    public override string DisplayName => "DeepSeek";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("deepseek");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("deepseek");
    public override string? DefaultEndpoint => "https://api.deepseek.com";
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.DeepSeekApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => null;

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.DeepSeekApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }
}
