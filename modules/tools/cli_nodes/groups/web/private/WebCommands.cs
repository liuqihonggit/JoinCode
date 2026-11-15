using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Web;

/// <summary>
/// web 分组的本地命令表 — 只用 BCL（HttpClient / FileStream），不引用引擎 / DI / IWebService。
/// <para>本地实现 2 个: download_file（分块并发 + 断点续传 + MD5）、web_to_markdown（HTML→Markdown）。</para>
/// <para>未登记的工具（web_fetch / web_search / web_browser）由 Program 转发宿主，exe 始终可用。</para>
/// 参数名与 [McpToolParameter] 特性逐一对应，说明不在此重复。
/// </summary>
internal static class WebCommands {
    private const int MinChunkBytes = 1024 * 1024;

    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["download_file"] = DownloadAsync,
            ["web_to_markdown"] = ToMarkdownAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static async Task<int> DownloadAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var url = Required(o, "url", "download_file");
        var target = Required(o, "file_path", "download_file");
        var threads = GetInt(o, "max_threads", 4);
        if (threads is < 1 or > 32)
            throw new ArgumentException($"max_threads 必须在 1..32 之间，当前 {threads}。正确写法: --max_threads 8");
        var resume = GetBool(o, "resume", true);

        var full = Path.GetFullPath(target);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var elapsed = Stopwatch.StartNew();
        using var http = CreateClient();
        var (total, acceptRanges) = await ProbeAsync(http, url, ct).ConfigureAwait(false);

        var existing = resume && File.Exists(full) ? new FileInfo(full).Length : 0;
        if (total > 0 && existing > total) existing = 0;

        long written;
        var remaining = total > 0 ? total - existing : -1;
        if (acceptRanges && total > 0 && threads > 1 && remaining > MinChunkBytes) {
            written = await DownloadParallelAsync(http, url, full, existing, total, threads, ct).ConfigureAwait(false);
        } else {
            written = await DownloadSingleAsync(http, url, full, existing, ct).ConfigureAwait(false);
        }

        elapsed.Stop();
        var sizeText = FormatSize(written);
        var md5 = ComputeMd5(full);
        var md5Text = md5 is null ? "" : $", MD5={md5}";
        Console.Out.WriteLine($"下载完成: {target} ({sizeText}, 耗时 {elapsed.Elapsed.TotalSeconds:F1}s{md5Text})");
        return 0;
    }

    /// <summary>探测长度与是否支持 Range。HEAD 被拒（403/405）时降级为单流下载，不当成失败。</summary>
    private static async Task<(long Total, bool AcceptRanges)> ProbeAsync(HttpClient http, string url, CancellationToken ct) {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (-1, false);
            var length = response.Content.Headers.ContentLength ?? -1;
            var ranges = response.Headers.AcceptRanges.Any(value => value.Contains("bytes", StringComparison.OrdinalIgnoreCase))
                || response.Content.Headers.ContentRange is not null;
            return (length, ranges);
        } catch (HttpRequestException) {
            return (-1, false);
        }
    }

    private static async Task<long> DownloadParallelAsync(
        HttpClient http, string url, string full, long start, long total, int threads, CancellationToken ct) {
        var remaining = total - start;
        var count = (int)Math.Clamp(remaining / MinChunkBytes, 1, threads);
        var chunk = (remaining + count - 1) / count;

        await using var stream = new FileStream(
            full, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1, FileOptions.Asynchronous);
        stream.SetLength(total);
        var handle = stream.SafeFileHandle;

        var tasks = new List<Task>(count);
        for (var i = 0; i < count; i++) {
            var from = start + (i * chunk);
            var to = Math.Min(total - 1, from + chunk - 1);
            if (from > to) continue;
            tasks.Add(DownloadChunkAsync(http, url, handle, from, to, ct));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return total;
    }

    private static async Task DownloadChunkAsync(
        HttpClient http, string url, SafeFileHandle handle, long from, long to, CancellationToken ct) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException($"服务端未按 Range 返回分块（HTTP {(int)response.StatusCode}），并发下载中止。");

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[81920];
        var offset = from;
        while (true) {
            var read = await body.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            RandomAccess.Write(handle, buffer.AsSpan(0, read), offset);
            offset += read;
        }
        if (offset != to + 1)
            throw new IOException($"分块 {from}-{to} 实际只收到 {offset - from} 字节，长度不匹配（服务端提前断开）。");
    }

    private static async Task<long> DownloadSingleAsync(
        HttpClient http, string url, string full, long existing, CancellationToken ct) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"下载失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}（{url}）");

        // 服务端忽略 Range 时返回 200 且是完整内容，必须截断重写，否则会拼出重复文件。
        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var mode = append ? FileMode.Append : FileMode.Create;
        await using var stream = new FileStream(full, mode, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await response.Content.CopyToAsync(stream, ct).ConfigureAwait(false);
        return stream.Length;
    }

    private static async Task<int> ToMarkdownAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var url = Required(o, "url", "web_to_markdown");
        var maxLength = GetInt(o, "max_length", 100_000);
        if (maxLength < 1)
            throw new ArgumentException($"max_length 必须为正整数，当前 {maxLength}。正确写法: --max_length 20000");

        using var http = CreateClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"抓取失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}（{url}）");

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var markdown = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || LooksLikeHtml(body)
            ? HtmlMarkdown.Convert(body)
            : body;

        if (markdown.Length > maxLength)
            markdown = markdown[..maxLength] + "\n\n[Content truncated due to length...]";
        Console.Out.Write(markdown);
        if (!markdown.EndsWith('\n')) Console.Out.WriteLine();
        return 0;
    }

    private static bool LooksLikeHtml(string body) {
        var head = body.AsSpan(0, Math.Min(body.Length, 2048));
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient() {
        var handler = new SocketsHttpHandler {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return http;
    }

    private static string? ComputeMd5(string path) {
        try {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>与宿主 ContentReplacementConstants.FormatCharCount 同一口径，避免同一文件两处显示不同大小。</summary>
    private static string FormatSize(long bytes) {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) {
            var whole = bytes / 1024;
            var frac = bytes % 1024 * 10 / 1024;
            return frac == 0 ? $"{whole}KB" : $"{whole}.{frac}KB";
        }
        if (bytes < 1024L * 1024 * 1024) {
            var whole = bytes / (1024 * 1024);
            var frac = bytes % (1024 * 1024) * 10 / (1024 * 1024);
            return frac == 0 ? $"{whole}MB" : $"{whole}.{frac}MB";
        }
        var gbWhole = bytes / (1024L * 1024 * 1024);
        var gbFrac = bytes % (1024L * 1024 * 1024) * 10 / (1024L * 1024 * 1024);
        return gbFrac == 0 ? $"{gbWhole}GB" : $"{gbWhole}.{gbFrac}GB";
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) {
        if (o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        throw new ArgumentException($"工具 '{toolId}' 缺少必填参数 --{key}。正确写法: {toolId} -h 查看参数。");
    }

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) {
        if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw, out var value)) return value;
        throw new ArgumentException($"参数 --{key} 需要整数，当前值 '{raw}'。");
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> o, string key, bool fallback) {
        if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => throw new ArgumentException($"参数 --{key} 需要布尔值，当前值 '{raw}'。")
        };
    }
}
