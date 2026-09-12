namespace JoinCode.Abstractions.Http;

/// <summary>
/// HTTP 客户端提供者 — 替代直接 new HttpClient()，支持环境变量一键切换 Real/Mock
/// <para>DI 容器构建后的服务应通过构造函数注入 IHttpClientProvider</para>
/// <para>DI 容器构建前的场景使用 HttpClientProviderFactory.Create()</para>
/// </summary>
public interface IHttpClientProvider
{
    /// <summary>
    /// 获取共享的 HttpClient 实例（适用于一般 HTTP 请求）
    /// </summary>
    HttpClient GetClient();

    /// <summary>
    /// 获取命名的 HttpClient 实例（适用于需要不同配置的场景）
    /// </summary>
    HttpClient GetClient(string name);
}
