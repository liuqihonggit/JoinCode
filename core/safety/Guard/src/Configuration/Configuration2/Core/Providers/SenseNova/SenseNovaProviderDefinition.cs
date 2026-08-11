
namespace Core.Configuration.Providers;

public sealed class SenseNovaProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    protected override string ProviderConfigKey => "sensenova";
    protected override string DefaultBaseUrl => "https://api.sensenova.cn/compatible-mode/v1/";

    public override VendorKind Vendor => VendorKind.Sensenova;
    public override string ProviderName => VendorKind.Sensenova.ToValue();
    public override string DisplayName => "商汤科技 SenseNova";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("sensenova");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("sensenova");
    public override string? DefaultEndpoint => "https://api.sensenova.cn/compatible-mode/v1";
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.SenseNovaApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => null;

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.SenseNovaApiKey.ToValue());
    }
}
