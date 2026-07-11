namespace Infrastructure.Http;

/// <summary>
/// HTTP 客户端提供者工厂 — 根据 JCC_HTTP_MODE 环境变量创建对应的 IHttpClientProvider 实例
/// 用于 DI 容器构建之前（如命令处理器）的场景，DI 容器构建后应通过注入 IHttpClientProvider 使用
/// </summary>
public static class HttpClientProviderFactory
{
    /// <summary>
    /// 根据环境变量创建 IHttpClientProvider 实例。
    /// JCC_HTTP_MODE=Mock → MockHttpClientProvider（拦截请求，0网络IO）
    /// 其他/未设置 → DefaultHttpClientProvider（真实网络，默认）
    /// </summary>
    public static IHttpClientProvider Create()
    {
        var httpMode = EnvHelper.Get(JccEnvVar.HttpMode);
        if (string.Equals(httpMode, "Mock", StringComparison.OrdinalIgnoreCase))
            return new MockHttpClientProvider();
        return new DefaultHttpClientProvider();
    }
}
