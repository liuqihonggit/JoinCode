namespace Services.Web;

/// <summary>
/// 搜索供应商配置辅助 — 从 IConfigRoot 按 searchProviders.{name}.{field} 路径读取，缺失时回退硬编码默认值（TASK054）。
/// <para>约定大于配置：配置文件未设置时回退硬编码环境变量名，保证现有测试不破坏。</para>
/// </summary>
public static class SearchProviderConfigHelper {
    /// <summary>
    /// 解析搜索供应商的环境变量名 — 从 IConfigRoot 读 searchProviders.{providerName}.{field}，未命中回退 <paramref name="fallback"/>。
    /// </summary>
    /// <param name="config">统一配置入口</param>
    /// <param name="providerName">供应商名（如 "tavily"、"serper"）</param>
    /// <param name="field">字段名（如 "apiKeyEnvVar"、"endpointEnvVar"）</param>
    /// <param name="fallback">配置未命中时的回退默认值（硬编码环境变量名）</param>
    /// <returns>配置值或回退默认值</returns>
    public static string ResolveEnvVar(IConfigRoot config, string providerName, string field, string fallback) {
        var path = $"searchProviders.{providerName}.{field}";
        if (config.TryGet<string>(path, out var value) && value is not null) {
            return value;
        }
        return fallback;
    }
}
