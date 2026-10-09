// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// EmbeddingModelDownloader 单元测试 — 验证缺失自动下载、双源竞赛、SHA256 校验。
/// </summary>
public sealed class EmbeddingModelDownloaderTests {

    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "jcc_embed_test_" + Guid.NewGuid().ToString("N")[..8]);

    private static string Sha256(byte[] data) {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToUpperInvariant();
    }

    private sealed class FakeHttpHandler : HttpMessageHandler {
        private readonly Dictionary<string, byte[]> _responses = new();
        public int GetCount { get; private set; }
        public int HeadCount { get; private set; }

        public void Setup(string url, byte[] content) => _responses[url] = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) {
            var url = req.RequestUri!.AbsoluteUri;
            if (req.Method == HttpMethod.Head) HeadCount++;
            else if (req.Method == HttpMethod.Get) GetCount++;

            if (!_responses.TryGetValue(url, out var bytes)) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = req });
            }
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            if (req.Method == HttpMethod.Head) {
                resp.Content = new ByteArrayContent(Array.Empty<byte>());
                resp.Content.Headers.ContentLength = bytes.Length;
            } else {
                resp.Content = new ByteArrayContent(bytes);
            }
            return Task.FromResult(resp);
        }
    }

    private sealed class FakeHttpProvider : IHttpClientProvider {
        private readonly HttpClient _client;
        public FakeHttpProvider(HttpMessageHandler handler) => _client = new HttpClient(handler);
        public HttpClientRef GetClient() => new(_client);
        public HttpClientRef GetClient(string name) => new(_client);
    }

    [Fact]
    public async Task EnsureAsync_BothFilesExist_ShaMatch_SkipsDownload() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var handler = new FakeHttpHandler();
        var http = new FakeHttpProvider(handler);
        var modelBytes = new byte[] { 1, 2, 3, 4, 5 };
        var vocabBytes = new byte[] { 9, 8, 7 };
        var modelPath = Path.Combine(Dir, EmbeddingModelDownloader.ModelFileName);
        var vocabPath = Path.Combine(Dir, EmbeddingModelDownloader.VocabFileName);
        fs.CreateDirectory(Dir);
        await fs.WriteAllBytesAsync(modelPath, modelBytes);
        await fs.WriteAllBytesAsync(vocabPath, vocabBytes);

        var downloader = new EmbeddingModelDownloader(
            modelSha256: Sha256(modelBytes),
            vocabSha256: Sha256(vocabBytes));
        await downloader.EnsureAsync(Dir, fs, http, new RangeDownloader(http, fs));

        Assert.Equal(0, handler.HeadCount);
        Assert.Equal(0, handler.GetCount);
    }

    [Fact]
    public async Task EnsureAsync_Missing_DownloadsFromReachableSource() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var modelBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x42, 0x00, 0x99, 0x88 };
        var vocabBytes = new byte[] { 0x0A, 0x0B, 0x0C, 0x0D, 0x0E };

        var handler = new FakeHttpHandler();
        foreach (var baseUrl in EmbeddingModelDownloader.BaseUrls) {
            handler.Setup(baseUrl + "onnx/model_quantized.onnx", modelBytes);
            handler.Setup(baseUrl + "vocab.txt", vocabBytes);
        }
        var http = new FakeHttpProvider(handler);

        var downloader = new EmbeddingModelDownloader(
            modelSha256: Sha256(modelBytes),
            vocabSha256: Sha256(vocabBytes));
        await downloader.EnsureAsync(Dir, fs, http, new RangeDownloader(http, fs));

        Assert.True(fs.FileExists(Path.Combine(Dir, EmbeddingModelDownloader.ModelFileName)));
        Assert.True(fs.FileExists(Path.Combine(Dir, EmbeddingModelDownloader.VocabFileName)));
        Assert.True(handler.GetCount >= 2);
    }

    [Fact]
    public async Task EnsureAsync_OnlyMirrorReachable_DownloadsFromMirror() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var modelBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var vocabBytes = new byte[] { 0x55, 0x66 };

        var handler = new FakeHttpHandler();
        var mirrorUrl = EmbeddingModelDownloader.BaseUrls[1];
        handler.Setup(mirrorUrl + "onnx/model_quantized.onnx", modelBytes);
        handler.Setup(mirrorUrl + "vocab.txt", vocabBytes);
        var http = new FakeHttpProvider(handler);

        var downloader = new EmbeddingModelDownloader(
            modelSha256: Sha256(modelBytes),
            vocabSha256: Sha256(vocabBytes));
        await downloader.EnsureAsync(Dir, fs, http, new RangeDownloader(http, fs));

        Assert.True(fs.FileExists(Path.Combine(Dir, EmbeddingModelDownloader.ModelFileName)));
    }

    [Fact]
    public async Task EnsureAsync_BadSha256_Throws() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var modelBytes = new byte[] { 1, 2, 3 };
        var vocabBytes = new byte[] { 4, 5, 6 };

        var handler = new FakeHttpHandler();
        foreach (var baseUrl in EmbeddingModelDownloader.BaseUrls) {
            handler.Setup(baseUrl + "onnx/model_quantized.onnx", modelBytes);
            handler.Setup(baseUrl + "vocab.txt", vocabBytes);
        }
        var http = new FakeHttpProvider(handler);

        var downloader = new EmbeddingModelDownloader(
            modelSha256: "0000000000000000000000000000000000000000000000000000000000000000",
            vocabSha256: Sha256(vocabBytes));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.EnsureAsync(Dir, fs, http, new RangeDownloader(http, fs)));
    }

    [Fact]
    public async Task EnsureAsync_AllSourcesUnreachable_Throws() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var handler = new FakeHttpHandler();
        var http = new FakeHttpProvider(handler);

        var downloader = new EmbeddingModelDownloader();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.EnsureAsync(Dir, fs, http, new RangeDownloader(http, fs)));
    }
}
