namespace JoinCode.Abstractions.Http;

/// <summary>
/// 韧性 HTTP 客户端提供者 — 扩展 IHttpClientProvider，添加超时+重试+熔断韧性
/// <para>消费方注入 IResilientHttpClientProvider 并调用 SendResilientAsync() 获得韧性保护</para>
/// <para>仍可通过 GetClient() 获取原始 HttpClient（无韧性，向后兼容）</para>
/// </summary>
public interface IResilientHttpClientProvider : IHttpClientProvider
{
    /// <summary>
    /// 发送带韧性的 HTTP 请求（超时+重试+熔断）
    /// </summary>
    Task<HttpResponseMessage> SendResilientAsync(
        HttpRequestMessage request,
        string operationName,
        CancellationToken ct = default);
}
