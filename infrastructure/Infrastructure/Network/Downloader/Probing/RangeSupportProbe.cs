namespace Infrastructure.Network.Downloader.Probing;

/// <summary>
/// Range 支持探测器 — 通过 HEAD 请求探测服务器是否支持断点续传
/// <para>探测策略:先 HEAD,若 405/403 回退 GET + Range: bytes=0-0</para>
/// <para>支持判定:HEAD 响应含 Accept-Ranges: bytes,或 GET Range 响应 206 Partial Content</para>
/// </summary>
internal sealed class RangeSupportProbe
{
    private readonly HttpClient _httpClient;

    internal RangeSupportProbe(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 探测 URL 是否支持 Range 请求
    /// </summary>
    internal async Task<RangeSupportResult> ProbeAsync(string url, CancellationToken ct = default)
    {
        using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
        using var headResp = await _httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (headResp.IsSuccessStatusCode)
            return ParseFromHeaders(headResp);

        if (headResp.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden)
            return await ProbeWithGetRangeAsync(url, ct).ConfigureAwait(false);

        return new RangeSupportResult(false, null, null, null);
    }

    private static RangeSupportResult ParseFromHeaders(HttpResponseMessage resp)
    {
        var supportsRange = resp.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase);
        var contentLength = resp.Content.Headers.ContentLength;
        var etag = resp.Headers.ETag?.Tag;
        var lastModified = resp.Content.Headers.LastModified;
        return new RangeSupportResult(supportsRange, contentLength, etag, lastModified);
    }

    private async Task<RangeSupportResult> ProbeWithGetRangeAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 0);
        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.PartialContent)
        {
            var totalLength = resp.Content.Headers.ContentRange?.Length
                ?? resp.Content.Headers.ContentLength;
            var etag = resp.Headers.ETag?.Tag;
            var lastModified = resp.Content.Headers.LastModified;
            return new RangeSupportResult(true, totalLength, etag, lastModified);
        }

        var fullLength = resp.Content.Headers.ContentLength;
        return new RangeSupportResult(false, fullLength, null, null);
    }
}
