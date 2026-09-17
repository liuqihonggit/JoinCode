
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
    public ProviderDefinitionRegistry(IModelConfigLoader modelConfigLoader, IFileSystem? fs = null, ILogger? logger = null)
    {
        var dict = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase);

        ApplyVendorFromSettings(dict, modelConfigLoader, fs, logger);

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

    private static void ApplyVendorFromSettings(Dictionary<string, IProviderDefinition> dict, IModelConfigLoader modelConfigLoader, IFileSystem? fs, ILogger? logger)
    {
        var settingsPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
        Diag.WriteLifecycle($"[PDR] settingsPath={settingsPath} JccDirectory={AppDataConstants.Paths.JccDirectory} UserProfile={Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");

        var fileSystem = fs ?? new IO.FileSystem.PhysicalFileSystem();
        if (!fileSystem.FileExists(settingsPath))
        {
            Diag.WriteLifecycle($"[PDR] FileExists=False → empty registry");
            return;
        }

        try
        {
            var json = fileSystem.ReadAllText(settingsPath);
            Diag.WriteLifecycle($"[PDR] ReadAllText OK, len={json.Length}");
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            var vendorNode = node?["vendor"];
            if (vendorNode is null)
            {
                Diag.WriteLifecycle($"[PDR] vendorNode is null → empty registry");
                return;
            }

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
            Diag.WriteLifecycle($"[PDR] Registered {dict.Count} vendors: {string.Join(", ", dict.Keys)}");
        }
        catch (System.Exception ex)
        {
            Diag.WriteLifecycle($"[PDR] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            logger?.LogWarning(ex, "ProviderDefinitionRegistry: settings.json 读取失败");
        }
    }
}
