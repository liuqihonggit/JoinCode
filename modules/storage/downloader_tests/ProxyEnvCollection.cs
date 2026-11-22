namespace Downloader.Tests;

/// <summary>
/// 代理环境变量互斥集合 — 串行执行，避免进程级环境变量污染并发测试。
/// <para>原因: RangeDownloaderProxyTests 会临时写 HTTPS_PROXY/HTTP_PROXY，
/// 而 RangeDownloader.ResolveProxyUrl 在 StartDownload 时读取同名环境变量。</para>
/// <para>xUnit 默认按类并行，若两者并发执行，其它下载测试会拿到测试用的假代理 URL
/// （如 http://env:8080）去连真实网络 → "不知道这样的主机" → result.Success=false 偶发红灯。</para>
/// <para>DisableParallelization=true 让该集合独占非并行阶段，与其它集合不会重叠。</para>
/// </summary>
[CollectionDefinition(ProxyEnvCollection.Name, DisableParallelization = true)]
public sealed class ProxyEnvCollection {
    /// <summary>集合名 — 所有依赖代理环境变量（读取或写入）的测试类都必须加入此集合</summary>
    internal const string Name = "DownloaderProxyEnvCollection";
}
