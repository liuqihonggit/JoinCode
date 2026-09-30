namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// ONNX 嵌入 IPC 客户端 — 启动独立 exe 进程，通过 stdin/stdout 通讯。
/// <para>主程序（AOT）通过此客户端调用非 AOT 的 ONNX exe，绕过 AOT 限制。</para>
/// <para>协议：4字节长度(LE) + JSON 请求 / 4字节长度(LE) + float[] 二进制响应。</para>
/// </summary>
public sealed class OnnxEmbeddingClient : IEmbeddingModel, IAsyncDisposable {

    private readonly Process _process;
    private readonly BinaryWriter _stdin;
    private readonly BinaryReader _stdout;
    private readonly int _dimensions;
    private readonly string _modelId;
    private readonly ILogger<OnnxEmbeddingClient>? _logger;
    private int _disposed;

    /// <summary>向量维度。</summary>
    public int Dimensions => _dimensions;

    /// <summary>模型标识（用于缓存键）。</summary>
    public string ModelId => _modelId;

    /// <summary>
    /// 构造 ONNX 嵌入客户端 — 启动独立 exe 进程。
    /// </summary>
    /// <param name="exePath">ONNX 嵌入 exe 路径。</param>
    /// <param name="fs">文件系统抽象（检查 exe 是否存在）。</param>
    /// <param name="dimensions">向量维度（默认 384）。</param>
    /// <param name="modelId">模型标识（默认 onnx-minilm-l6-v2）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public OnnxEmbeddingClient(
        string exePath,
        IFileSystem fs,
        int dimensions = 384,
        string modelId = "onnx-minilm-l6-v2",
        ILogger<OnnxEmbeddingClient>? logger = null) {
        ArgumentNullException.ThrowIfNull(exePath);
        ArgumentNullException.ThrowIfNull(fs);
        if (!fs.FileExists(exePath)) {
            throw new FileNotFoundException("ONNX 嵌入 exe 不存在", exePath);
        }

        _dimensions = dimensions;
        _modelId = modelId;
        _logger = logger;

        var psi = new ProcessStartInfo {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi };
        if (!_process.Start()) {
            throw new InvalidOperationException("无法启动 ONNX 嵌入 exe 进程");
        }

        _stdin = new BinaryWriter(_process.StandardInput.BaseStream, System.Text.Encoding.UTF8);
        _stdout = new BinaryReader(_process.StandardOutput.BaseStream, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 嵌入单个文本 → 向量。
    /// </summary>
    /// <param name="text">待嵌入文本。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>嵌入向量。</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(text);
        var results = await EmbedBatchAsync([text], ct).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// 批量嵌入 — 通过 IPC 发送至 ONNX exe 推理。
    /// </summary>
    /// <param name="texts">待嵌入文本列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>向量数组。</returns>
    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return Task.FromResult(Array.Empty<float[]>());
        ct.ThrowIfCancellationRequested();

        var jsonBytes = SerializeRequest(texts);

        lock (_stdin) {
            _stdin.Write(jsonBytes.Length);
            _stdin.Write(jsonBytes);
            _stdin.Flush();

            var responseLength = _stdout.ReadInt32();
            var responseBytes = _stdout.ReadBytes(responseLength);

            var batchSize = texts.Count;
            var dims = _dimensions;
            var vectors = new float[batchSize][];
            for (var i = 0; i < batchSize; i++) {
                var vector = new float[dims];
                Buffer.BlockCopy(responseBytes, i * dims * 4, vector, 0, dims * 4);
                vectors[i] = vector;
            }
            return Task.FromResult(vectors);
        }
    }

    /// <summary>
    /// 序列化嵌入请求 — AOT 源生成器，无反射。
    /// </summary>
    private static byte[] SerializeRequest(IReadOnlyList<string> texts) {
        var request = new EmbedIpcRequest {
            Id = 0,
            Texts = texts.ToList()
        };
        return JsonSerializer.SerializeToUtf8Bytes(request, EmbedIpcJsonContext.Default.EmbedIpcRequest);
    }

    /// <summary>释放进程资源。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try {
            _stdin.Close();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "关闭 stdin 失败");
        }
        try {
            if (!_process.HasExited) {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "终止 ONNX exe 进程失败");
        }
        _process.Dispose();
    }

    /// <summary>异步释放进程资源。</summary>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
