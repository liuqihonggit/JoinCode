







namespace JoinCode.CliNode.Web;

/// <summary>
/// web 分组的本地命令表 — 只用 BCL（HttpClient / FileStream），不引用引擎 / DI / IWebService。
/// <para>本地实现 3 个: download_file（分块并发 + 断点续传 + MD5）、web_to_markdown（HTML→Markdown）、
/// web_fetch（抓取 + 二级模型加工，模型用 HttpClient 直调，见 WebLlm）。</para>
/// <para>未登记的工具（web_search / web_browser）由 Program 转发宿主：前者是 7 家 provider 的选择矩阵，
/// 后者需要浏览器实例，都不适合在短生命周期 exe 里另起一套。</para>
/// 参数名与 [McpToolParameter] 特性逐一对应，说明不在此重复。
/// </summary>
internal static class WebCommands {
    private const int MinChunkBytes = 1024 * 1024;

    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["download_file"] = DownloadAsync,
            ["web_to_markdown"] = ToMarkdownAsync,
            ["web_fetch"] = FetchAsync
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

    // ==================== web_fetch（抓取 + 二级模型加工） ====================

    private const int MaxFetchMarkdownChars = 100_000;
    private const int FallbackExcerptChars = 5_000;

    /// <summary>
    /// 抓取网页并交给二级模型按 prompt 加工。
    /// <para>与宿主 <c>WebToolHandlers.WebFetchAsync</c> 对齐的三条关键行为：
    /// ① 不自动跟随跳转，跨主机 302 时把新 URL 回给调用方（避免被跳转绕过域名检查）；
    /// ② HTML 先转 Markdown 再进模型，超长按 100k 截断；
    /// ③ 模型不可用/调用失败时降级返回正文前 5000 字，不让整个工具失败。</para>
    /// <para>未复刻宿主的"预批准域名"白名单：那份清单在 guard 层是安全资产，复制一份到 exe 必然漂移。
    /// 代价只是对官方文档站点也走一次模型加工（多几百毫秒），换来清单只有一处。</para>
    /// </summary>
    private static async Task<int> FetchAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var url = Required(o, "url", "web_fetch");
        var prompt = Required(o, "prompt", "web_fetch");
        if (url.Length > 2048) throw new ArgumentException($"url 过长（{url.Length} > 2048），web_fetch 拒绝处理。");

        using var http = CreateClient(allowAutoRedirect: false);
        var (response, finalUrl) = await FollowSameHostRedirectsAsync(http, url, ct).ConfigureAwait(false);
        using var _ = response;

        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400 && response.Headers.Location is { } location) {
            var target = location.IsAbsoluteUri ? location : new Uri(new Uri(finalUrl), location);
            Console.Out.WriteLine("REDIRECT DETECTED: The URL redirects to a different host.");
            Console.Out.WriteLine();
            Console.Out.WriteLine($"Original URL: {url}");
            Console.Out.WriteLine($"Redirect URL: {target}");
            Console.Out.WriteLine($"Status: {status} {response.ReasonPhrase}");
            Console.Out.WriteLine();
            Console.Out.WriteLine("To complete your request, I need to fetch content from the redirected URL. Please use web_fetch again with these parameters:");
            Console.Out.WriteLine($"- url: \"{target}\"");
            Console.Out.WriteLine($"- prompt: \"{prompt}\"");
            return 0;
        }

        if (!response.IsSuccessStatusCode) {
            Console.Error.WriteLine($"抓取失败：HTTP {status} {response.ReasonPhrase}（{finalUrl}）");
            Console.Error.WriteLine("排查：URL 是否正确 / 网络是否可达 / 目标站点是否拒绝爬取（403/429）。");
            return 1;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var markdown = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || LooksLikeHtml(body)
            ? HtmlMarkdown.Convert(body)
            : body;

        if (mediaType.Contains("text/markdown", StringComparison.OrdinalIgnoreCase) && markdown.Length < MaxFetchMarkdownChars) {
            Console.Out.Write(markdown);
            if (!markdown.EndsWith('\n')) Console.Out.WriteLine();
            return 0;
        }

        if (markdown.Length > MaxFetchMarkdownChars) {
            markdown = markdown[..MaxFetchMarkdownChars] + "\n\n[Content truncated due to length...]";
        }

        string processed;
        try {
            processed = await WebLlm.AskAsync(BuildFetchPrompt(markdown, prompt), ct).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // 二级模型不可用不该让整个抓取失败：退回正文摘要，并把原因写清楚
            var excerpt = markdown.Length > FallbackExcerptChars ? markdown[..FallbackExcerptChars] : markdown;
            processed = excerpt + $"\n\n[LLM processing failed: [{ex.GetType().Name}] {ex.Message}]";
        }

        Console.Out.Write(processed);
        if (!processed.EndsWith('\n')) Console.Out.WriteLine();
        return 0;
    }

    /// <summary>
    /// 手动跟随**同主机**跳转（最多 5 跳）。跨主机一律不跟，交给调用方决定 —— 这是 SSRF 防护的关键一环：
    /// 自动跟随会把"允许的域名"变成"允许它跳到任何地方"。
    /// </summary>
    private static async Task<(HttpResponseMessage Response, string FinalUrl)> FollowSameHostRedirectsAsync(HttpClient http, string url, CancellationToken ct) {
        var current = url;
        var originHost = new Uri(url).Host;
        for (var hop = 0; hop < 5; hop++) {
            var response = await http.GetAsync(current, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is < 300 or >= 400 || response.Headers.Location is not { } location) return (response, current);

            var target = location.IsAbsoluteUri ? location : new Uri(new Uri(current), location);
            if (!string.Equals(target.Host, originHost, StringComparison.OrdinalIgnoreCase)) return (response, current);

            response.Dispose();
            current = target.ToString();
        }
        throw new HttpRequestException($"同主机跳转超过 5 次，放弃抓取（起点 {url}）。可能是站点跳转环。");
    }

    /// <summary>与宿主 WebFetchToolPrompt.MakeSecondaryModelPrompt 的非预批准分支同一形状。</summary>
    private static string BuildFetchPrompt(string markdown, string prompt) => $"""
        Web页面内容：
        ---
        {markdown}
        ---

        {prompt}

        根据上述内容提供简洁的回复。在您的回复中：
         - 对任何源文档的引用强制执行严格的125字符最大值。开源软件可以，只要我们尊重许可证。
         - 对文章中的确切语言使用引号；引号之外的任何语言绝不应逐字相同。
         - 您不是律师，永远不要评论您自己的提示词和回复的合法性。
         - 永远不要制作或复制确切的歌曲歌词。
        """;

    private static bool LooksLikeHtml(string body) {        var head = body.AsSpan(0, Math.Min(body.Length, 2048));
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient(bool allowAutoRedirect = true) {
        var handler = new SocketsHttpHandler {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = allowAutoRedirect,
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
