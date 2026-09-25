
namespace Core.Configuration.Providers;

/// <summary>
/// 供应商定义注册表 — 从 settings.json 的 vendor 节点构建，配置大于内置
/// 按 protocol 字段分派：openai-compatible → OpenAiCompatibleProviderDefinition，anthropic → AnthropicProviderDefinition
/// Azure 始终保留（OAuth + 复合认证特殊逻辑）
/// </summary>
public sealed class ProviderDefinitionRegistry : IProviderDefinitionRegistry {
    private FrozenDictionary<string, IProviderDefinition> _definitions = new Dictionary<string, IProviderDefinition>(0, StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造供应商定义注册表 — 从 settings.json 的 vendor 节点构建，并始终保留 Azure 供应商
    /// <para>IFileSystem 异步化后构造函数 fire-and-forget 异步初始化；PhysicalFileSystem UTF-8 走 mmap 同步完成，实际不阻塞。</para>
    /// </summary>
    public ProviderDefinitionRegistry(IModelConfigLoader modelConfigLoader, IFileSystem? fs = null, ILogger? logger = null) {
        _ = InitializeAsync(modelConfigLoader, fs, logger);
    }

    private async Task InitializeAsync(IModelConfigLoader modelConfigLoader, IFileSystem? fs, ILogger? logger) {
        var dict = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        await ApplyVendorFromSettingsAsync(dict, modelConfigLoader, fs, logger).ConfigureAwait(false);
        _definitions = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按名称获取供应商定义 — 未注册返回 null
    /// </summary>
    public IProviderDefinition? TryGet(string providerName) {
        return _definitions.GetValueOrDefault(providerName);
    }

    /// <summary>
    /// 供应商是否已注册 — O(1) 查询
    /// </summary>
    /// <param name="providerName">供应商名称</param>
    /// <returns>已注册返回 true</returns>
    public bool Contains(string providerName) {
        ArgumentNullException.ThrowIfNull(providerName);
        return _definitions.ContainsKey(providerName);
    }

    /// <summary>
    /// 获取已注册供应商名称的快照拷贝 — 用于枚举/显示
    /// </summary>
    /// <returns>供应商名称数组快照</returns>
    public string[] GetRegisteredProviders() {
        return _definitions.Keys.ToArray();
    }

    private static async ValueTask ApplyVendorFromSettingsAsync(Dictionary<string, IProviderDefinition> dict, IModelConfigLoader modelConfigLoader, IFileSystem? fs, ILogger? logger) {
        var settingsPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
        logger?.LogDebug("PDR settingsPath={Path} JccDirectory={Dir} UserProfile={Profile}", settingsPath, AppDataConstants.Paths.JccDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        var fileSystem = fs ?? new IO.FileSystem.PhysicalFileSystem();
        if (!fileSystem.FileExists(settingsPath)) {
            logger?.LogDebug("PDR FileExists=False → empty registry");
            return;
        }

        var node = await DirtyReadRetry.ReadWithRetryAsync(
            () => fileSystem.ReadAllText(settingsPath).AsTask(),
            json => System.Text.Json.Nodes.JsonNode.Parse(json),
            settingsPath,
            logger: logger).ConfigureAwait(false);

        if (node is null) return;

        var vendorNode = node["vendor"];
        if (vendorNode is null) {
            logger?.LogDebug("PDR vendorNode is null → empty registry");
            return;
        }

        try {
            foreach (var property in vendorNode.AsObject()) {
                var vendorName = property.Key;
                var profileNode = property.Value;
                if (profileNode is null) continue;

                var protocol = profileNode["protocol"]?.GetValue<string>() ?? ProtocolKindEnumConstants.OpenAiCompatible;
                var apiKeyEnvVar = profileNode["apiKeyEnvVar"]?.GetValue<string>();
                var anthropicBeta = profileNode["anthropicBeta"]?.GetValue<string>();

                dict[vendorName] = protocol switch {
                    var p when string.Equals(p, ProtocolKindEnumConstants.Anthropic, StringComparison.OrdinalIgnoreCase)
                        => (IProviderDefinition)new AnthropicCompatibleProviderDefinition(modelConfigLoader, vendorName, apiKeyEnvVar, anthropicBeta),
                    var p when string.Equals(p, ProtocolKindEnumConstants.Azure, StringComparison.OrdinalIgnoreCase)
                        => new AzureProviderDefinition(modelConfigLoader),
                    var p when string.Equals(p, ProtocolKindEnumConstants.Jev, StringComparison.OrdinalIgnoreCase)
                        => new JevProviderDefinition(modelConfigLoader, vendorName, apiKeyEnvVar),
                    _ => new OpenAiCompatibleProviderDefinition(modelConfigLoader, vendorName, apiKeyEnvVar),
                };
            }
            logger?.LogDebug("PDR Registered {Count} vendors: {Vendors}", dict.Count, string.Join(", ", dict.Keys));
        } catch (System.Exception ex) {
            logger?.LogWarning(ex, "ProviderDefinitionRegistry: settings.json 解析失败");
        }
    }
}