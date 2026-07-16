
namespace Core.Configuration.Providers;

public sealed class AgnesProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    protected override string ProviderConfigKey => "agnes";
    protected override string DefaultBaseUrl => "https://apihub.agnes-ai.com/v1/";

    public override ProviderKind Kind => ProviderKind.Agnes;
    public override string ProviderName => ProviderKind.Agnes.ToValue();
    public override string DisplayName => "Agnes";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("agnes");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("agnes");
    public override string? DefaultEndpoint => "https://apihub.agnes-ai.com/v1";
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.AgnesApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => null;

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AgnesApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }
}
