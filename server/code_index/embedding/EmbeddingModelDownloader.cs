namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 向量模型自动下载器 — 模型缺失时从预配置远程自动拉取（对齐 git submodule update --init 行为）。
/// <para>双源竞赛：HuggingFace 官方 + hf-mirror 镜像，并发 HEAD 探测，先 200 的源胜出。</para>
/// <para>国内网络官方超时则镜像胜出，国外官方快则官方胜出，无需检测 IP 地理位置。</para>
/// <para>下载后 SHA256 校验，不匹配抛异常。> ADR: 0124</para>
/// </summary>
public sealed class EmbeddingModelDownloader {

    /// <summary>模型文件名。</summary>
    public const string ModelFileName = "model_quantized.onnx";

    /// <summary>词表文件名。</summary>
    public const string VocabFileName = "vocab.txt";

    /// <summary>默认目标目录 — %AppData%/jcc/embedding/，下载与识别共用此路径（唯一数据源）。</summary>
    public static string DefaultTargetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "jcc", "embedding");

    /// <summary>下载源基础 URL（官方 + 镜像），竞赛选最快。</summary>
    public static readonly string[] BaseUrls = [
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/",
        "https://hf-mirror.com/Xenova/all-MiniLM-L6-v2/resolve/main/",
    ];

    internal const string DefaultModelSha256 = "AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1";
    internal const string DefaultVocabSha256 = "07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3";

    private readonly string _modelSha256;
    private readonly string _vocabSha256;
    private readonly ILogger<EmbeddingModelDownloader>? _logger;

    /// <summary>
    /// 构造向量模型下载器。
    /// </summary>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="modelSha256">模型 SHA256（可选，默认用生产常量）。</param>
    /// <param name="vocabSha256">词表 SHA256（可选，默认用生产常量）。</param>
    public EmbeddingModelDownloader(
        ILogger<EmbeddingModelDownloader>? logger = null,
        string? modelSha256 = null,
        string? vocabSha256 = null) {
        _logger = logger;
        _modelSha256 = modelSha256 ?? DefaultModelSha256;
        _vocabSha256 = vocabSha256 ?? DefaultVocabSha256;
    }

    /// <summary>
    /// 确保模型文件存在 — 缺失则自动下载，存在且校验通过则跳过。
    /// </summary>
    /// <param name="targetDir">目标目录（如 %AppData%/jcc/embedding/）。</param>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="http">HTTP 客户端提供者。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task EnsureAsync(
        string targetDir,
        IFileSystem fs,
        IHttpClientProvider http,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(targetDir);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(http);
        ct.ThrowIfCancellationRequested();

        var modelPath = Path.Combine(targetDir, ModelFileName);
        var vocabPath = Path.Combine(targetDir, VocabFileName);

        if (await IsFileValidAsync(modelPath, _modelSha256, fs, ct).ConfigureAwait(false)
            && await IsFileValidAsync(vocabPath, _vocabSha256, fs, ct).ConfigureAwait(false)) {
            _logger?.LogDebug("向量模型文件已存在且校验通过，跳过下载");
            return;
        }

        var baseUrl = await SelectSourceAsync(http, ct).ConfigureAwait(false);
        _logger?.LogInformation("选中下载源: {Url}", baseUrl);

        if (!await IsFileValidAsync(modelPath, _modelSha256, fs, ct).ConfigureAwait(false)) {
            Console.Error.WriteLine($"[code-index] 正在下载向量模型 {ModelFileName} ...");
            await DownloadAndVerifyAsync(baseUrl + "onnx/" + ModelFileName, modelPath, _modelSha256, fs, http, ct).ConfigureAwait(false);
        }
        if (!await IsFileValidAsync(vocabPath, _vocabSha256, fs, ct).ConfigureAwait(false)) {
            Console.Error.WriteLine($"[code-index] 正在下载词表 {VocabFileName} ...");
            await DownloadAndVerifyAsync(baseUrl + VocabFileName, vocabPath, _vocabSha256, fs, http, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> IsFileValidAsync(string path, string expectedSha, IFileSystem fs, CancellationToken ct) {
        if (!fs.FileExists(path)) return false;
        try {
            await using var stream = fs.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(hash), expectedSha, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    private static async Task<string> SelectSourceAsync(IHttpClientProvider http, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        var client = http.GetClient();
        var tasks = BaseUrls.Select(async url => {
            try {
                using var req = new HttpRequestMessage(HttpMethod.Head, url + "onnx/" + ModelFileName);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                return resp.IsSuccessStatusCode ? url : null;
            } catch {
                return null;
            }
        }).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.FirstOrDefault(r => r is not null)
            ?? throw new InvalidOperationException("所有向量模型下载源均不可达（HuggingFace 官方 + hf-mirror 镜像均失败）");
    }

    private static async Task DownloadAndVerifyAsync(
        string url, string targetPath, string expectedSha,
        IFileSystem fs, IHttpClientProvider http, CancellationToken ct) {
        var bytes = await http.GetClient().GetByteArrayAsync(url, ct).ConfigureAwait(false);
        using var sha = SHA256.Create();
        var actualHash = sha.ComputeHash(bytes);
        var actualSha = Convert.ToHexString(actualHash);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"下载文件 SHA256 校验失败: {url} 期望 {expectedSha} 实际 {actualSha}");
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) fs.CreateDirectory(dir);
        await fs.WriteAllBytesAsync(targetPath, bytes, ct).ConfigureAwait(false);
    }
}
