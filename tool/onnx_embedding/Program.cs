namespace OnnxEmbedding;

/// <summary>
/// ONNX 嵌入独立 exe 入口 — stdin/stdout 二进制 IPC 循环。
/// <para>协议：4字节长度(LE) + JSON 请求 / 4字节长度(LE) + float[] 二进制响应。</para>
/// </summary>
internal static class Program {

    /// <summary>入口点 — 加载模型并进入 IPC 循环。</summary>
    /// <param name="args">命令行参数（未使用）。</param>
    /// <returns>0 成功，1 错误。</returns>
    private static int Main(string[] args) {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "jcc", "embedding");
        var modelPath = Path.Combine(appData, "model_quantized.onnx");
        var vocabPath = Path.Combine(appData, "vocab.txt");

        if (!File.Exists(modelPath)) {
            Console.Error.WriteLine($"模型文件不存在: {modelPath}");
            return 1;
        }
        if (!File.Exists(vocabPath)) {
            Console.Error.WriteLine($"词表文件不存在: {vocabPath}");
            return 1;
        }

        using var embedder = new OnnxEmbedder(modelPath, vocabPath);
        Console.Error.WriteLine($"模型加载完成: dim={embedder.Dimensions}");

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        while (true) {
            var request = ReadRequest(stdin);
            if (request is null) break;

            var vectors = embedder.EmbedBatch(request.Texts);
            WriteResponse(stdout, vectors);
        }

        return 0;
    }

    private static EmbedRequest? ReadRequest(Stream stdin) {
        var lengthBytes = new byte[4];
        if (stdin.Read(lengthBytes, 0, 4) != 4) return null;
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > 100 * 1024 * 1024) return null;

        var jsonBytes = new byte[length];
        if (stdin.Read(jsonBytes, 0, length) != length) return null;

        var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
        return JsonSerializer.Deserialize<EmbedRequest>(json);
    }

    private static void WriteResponse(Stream stdout, float[][] vectors) {
        var batchSize = vectors.Length;
        var dims = vectors.Length > 0 ? vectors[0].Length : 0;
        var responseBytes = new byte[batchSize * dims * 4];
        for (var i = 0; i < batchSize; i++) {
            Buffer.BlockCopy(vectors[i], 0, responseBytes, i * dims * 4, dims * 4);
        }

        var lengthBytes = BitConverter.GetBytes(responseBytes.Length);
        stdout.Write(lengthBytes, 0, 4);
        stdout.Write(responseBytes, 0, responseBytes.Length);
        stdout.Flush();
    }
}
