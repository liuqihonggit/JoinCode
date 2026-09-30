using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

Console.WriteLine("=== ONNX 嵌入详尽基准测试 ===");
Console.WriteLine($"CPU 线程数: {Environment.ProcessorCount}");
Console.WriteLine();

var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jcc", "embedding");
var modelFull = Path.Combine(appData, "all-MiniLM-L6-v2.onnx");
var modelQuant = Path.Combine(appData, "model_quantized.onnx");

var seqLens = new[] { 32, 48, 64 };
var batchSizes = new[] { 32, 64, 128 };

Console.WriteLine($"原版模型: {(File.Exists(modelFull) ? $"{new FileInfo(modelFull).Length / 1024.0 / 1024.0:F1}MB" : "不存在")}");
Console.WriteLine($"量化模型: {(File.Exists(modelQuant) ? $"{new FileInfo(modelQuant).Length / 1024.0 / 1024.0:F1}MB" : "不存在")}");
Console.WriteLine();

var results = new List<BenchResult>();

if (File.Exists(modelFull)) {
    Console.WriteLine("--- 原版模型 (86MB FP32) ---");
    RunBenchmark("CPU默认", modelFull, CreateCpuDefaultSession, seqLens, batchSizes, results);
    RunBenchmark("CPU优化", modelFull, CreateCpuOptimizedSession, seqLens, batchSizes, results);
    RunBenchmark("DirectML", modelFull, CreateDirectMlSession, seqLens, batchSizes, results);
}

if (File.Exists(modelQuant)) {
    Console.WriteLine("--- 量化模型 (22MB INT8) ---");
    RunBenchmark("量化CPU默认", modelQuant, CreateCpuDefaultSession, seqLens, batchSizes, results);
    RunBenchmark("量化CPU优化", modelQuant, CreateCpuOptimizedSession, seqLens, batchSizes, results);
    RunBenchmark("量化DirectML", modelQuant, CreateDirectMlSession, seqLens, batchSizes, results);
}

Console.WriteLine();
Console.WriteLine("=== 汇总（5000块估算时间，目标<10s）===");
Console.WriteLine($"{"配置",20} {"seq",4} {"batch",6} {"ms/次",8} {"5000块",8} {"达标",6}");
Console.WriteLine(new string('-', 60));
foreach (var r in results.OrderBy(x => x.Est5000)) {
    var ok = r.Est5000 < 10 ? "✅" : r.Est5000 < 12 ? "≈" : "❌";
    Console.WriteLine($"{r.Config,20} {r.SeqLen,4} {r.BatchSize,6} {r.MsPerRun,8:F1} {r.Est5000,7:F1}s {ok,6}");
}

Console.WriteLine();
var best = results.OrderBy(x => x.Est5000).First();
Console.WriteLine($"最佳配置: {best.Config} seq={best.SeqLen} batch={best.BatchSize} → 5000块 {best.Est5000:F1}s");

static InferenceSession CreateCpuDefaultSession(string modelPath) => new(modelPath);

static InferenceSession CreateCpuOptimizedSession(string modelPath) {
    var options = new SessionOptions {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_PARALLEL,
    };
    options.AddSessionConfigEntry("intra_op_num_threads", Environment.ProcessorCount.ToString());
    return new InferenceSession(modelPath, options);
}

static InferenceSession CreateDirectMlSession(string modelPath) {
    var options = new SessionOptions {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
    };
    try {
        options.AppendExecutionProvider_DML(0);
        return new InferenceSession(modelPath, options);
    } catch (Exception ex) {
        Console.WriteLine($"  DirectML 不可用: {ex.Message.Split('\n')[0]}");
        throw;
    }
}

static void RunBenchmark(string configName, string modelPath, Func<string, InferenceSession> sessionFactory, int[] seqLens, int[] batchSizes, List<BenchResult> results) {
    InferenceSession session;
    try {
        var sw = Stopwatch.StartNew();
        session = sessionFactory(modelPath);
        sw.Stop();
        Console.WriteLine($"  {configName}: 模型加载 {sw.Elapsed.TotalMilliseconds:F0}ms, 输入=[{string.Join(",", session.InputMetadata.Keys)}]");
    } catch {
        return;
    }

    using (session) {
        var inputNames = session.InputMetadata.Keys.ToList();
        var hasTokenTypeIds = inputNames.Contains("token_type_ids");

        foreach (var seqLen in seqLens) {
            foreach (var batchSize in batchSizes) {
                var inputs = CreateInputs(batchSize, seqLen, hasTokenTypeIds);
                for (var w = 0; w < 3; w++) { using var _ = session.Run(inputs); }
                var sw = Stopwatch.StartNew();
                var runs = 10;
                for (var i = 0; i < runs; i++) { using var _ = session.Run(inputs); }
                sw.Stop();
                var msPerRun = sw.Elapsed.TotalMilliseconds / runs;
                var est5000 = 5000.0 / batchSize * msPerRun / 1000;
                results.Add(new BenchResult(configName, seqLen, batchSize, msPerRun, est5000));
                Console.WriteLine($"    seq={seqLen,3} batch={batchSize,3}: {msPerRun,7:F1} ms/次 → 5000块 {est5000,5:F1}s");
            }
        }
    }
    Console.WriteLine();
}

static List<NamedOnnxValue> CreateInputs(int batchSize, int seqLen, bool hasTokenTypeIds) {
    var inputIds = new DenseTensor<long>(new long[batchSize * seqLen], new[] { batchSize, seqLen });
    var attentionMask = new DenseTensor<long>(new long[batchSize * seqLen], new[] { batchSize, seqLen });
    for (var i = 0; i < batchSize; i++)
        for (var j = 0; j < seqLen; j++) {
            inputIds[i, j] = Random.Shared.Next(1, 30000);
            attentionMask[i, j] = 1;
        }
    var inputs = new List<NamedOnnxValue> {
        NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
        NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
    };
    if (hasTokenTypeIds) {
        var tokenTypeIds = new DenseTensor<long>(new long[batchSize * seqLen], new[] { batchSize, seqLen });
        inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));
    }
    return inputs;
}

internal sealed record BenchResult(string Config, int SeqLen, int BatchSize, double MsPerRun, double Est5000);
