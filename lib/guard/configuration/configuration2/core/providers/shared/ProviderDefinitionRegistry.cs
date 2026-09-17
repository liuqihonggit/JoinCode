
namespace Core.Configuration.Providers;

/// <summary>
/// 供应商定义注册表 — 从 settings.json 的 vendor 节点构建，配置大于内置
/// 按 protocol 字段分派：openai-compatible → OpenAiCompatibleProviderDefinition，anthropic → AnthropicProviderDefinition
/// Azure 始终保留（OAuth + 复合认证特殊逻辑）
/// </summary>
public sealed class ProviderDefinitionRegistry : IProviderDefinitionRegistry
{
    private readonly FrozenDictionary<string, IProviderDefinition> _definitions;

    /// <summary>
    /// 构造供应商定义注册表 — 从 settings.json 的 vendor 节点构建，并始终保留 Azure 供应商
    /// </summary>
    public ProviderDefinitionRegistry(IModelConfigLoader modelConfigLoader, IFileSystem? fs = null)
    {
        var dict = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase);

        ApplyVendorFromSettings(dict, modelConfigLoader, fs);

        _definitions = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按名称获取供应商定义 — 未注册返回 null
    /// </summary>
    public IProviderDefinition? TryGet(string providerName)
    {
        return _definitions.GetValueOrDefault(providerName);
    }

    /// <summary>
    /// 已注册的供应商名称集合
    /// </summary>
    public IReadOnlyCollection<string> RegisteredProviders => _definitions.Keys;

    private static void ApplyVendorFromSettings(Dictionary<string, IProviderDefinition> dict, IModelConfigLoader modelConfigLoader, IFileSystem? fs)
    {
        var settingsPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);

        var fileSystem = fs ?? new IO.FileSystem.PhysicalFileSystem();
        if (!fileSystem.FileExists(settingsPath))
            return;

        try
        {
            var json = fileSystem.ReadAllText(settingsPath);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            var vendorNode = node?["vendor"];
            if (vendorNode is null) return;

            foreach (var property in vendorNode.AsObject())
            {
                var vendorName = property.Key;
                var profileNode = property.Value;
                if (profileNode is null) continue;

                var protocol = profileNode["protocol"]?.GetValue<string>() ?? ProtocolKindEnumConstants.OpenAiCompatible;
                var apiKeyEnvVar = profileNode["apiKeyEnvVar"]?.GetValue<string>();
                var anthropicBeta = profileNode["anthropicBeta"]?.GetValue<string>();

                dict[vendorName] = protocol switch
                {
                    var p when string.Equals(p, ProtocolKindEnumConstants.Anthropic, StringComparison.OrdinalIgnoreCase) 
                        => (IProviderDefinition)new AnthropicCompatibleProviderDefinition(modelConfigLoader, vendorName, apiKeyEnvVar, anthropicBeta),
                    var p when string.Equals(p, ProtocolKindEnumConstants.Azure, StringComparison.OrdinalIgnoreCase) 
                        => new AzureProviderDefinition(modelConfigLoader),
                    _ => new OpenAiCompatibleProviderDefinition(modelConfigLoader, vendorName, apiKeyEnvVar),
                };
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"ProviderDefinitionRegistry: settings.json 读取失败: {ex.Message}");
        }
    }
}
