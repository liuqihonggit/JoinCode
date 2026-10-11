using System.Numerics;

const int count = 10_000;
const int dims = 256;
const int topK = 10;
const int warmup = 100;
const int iterations = 1000;

Console.WriteLine($"=== SIMD 向量搜索验证 ===");
Console.WriteLine($"向量数量: {count}, 维度: {dims}, TopK: {topK}");
Console.WriteLine($"Vector<float>.Count: {Vector<float>.Count} (SIMD 宽度)");
Console.WriteLine();

var random = new Random(42);
var vectors = new float[count][];
for (var i = 0; i < count; i++) {
    var v = new float[dims];
    for (var j = 0; j < dims; j++) v[j] = (float)random.NextDouble();
    var norm = MathF.Sqrt(v.Sum(x => x * x));
    for (var j = 0; j < dims; j++) v[j] /= norm;
    vectors[i] = v;
}

var query = new float[dims];
for (var j = 0; j < dims; j++) query[j] = (float)random.NextDouble();
var qnorm = MathF.Sqrt(query.Sum(x => x * x));
for (var j = 0; j < dims; j++) query[j] /= qnorm;

for (var w = 0; w < warmup; w++) {
    BruteForceSimd(query, vectors, topK);
}
var sw = Stopwatch.StartNew();
List<(int Id, float Score)> simdResult = default!;
for (var it = 0; it < iterations; it++) {
    simdResult = BruteForceSimd(query, vectors, topK);
}
sw.Stop();
var simdMs = sw.Elapsed.TotalMilliseconds / iterations;
Console.WriteLine($"SIMD 暴力搜索: {simdMs:F3} ms/次 (平均 {iterations} 次)");

for (var w = 0; w < warmup; w++) {
    BruteForceScalar(query, vectors, topK);
}
sw.Restart();
List<(int Id, float Score)> scalarResult = default!;
for (var it = 0; it < iterations; it++) {
    scalarResult = BruteForceScalar(query, vectors, topK);
}
sw.Stop();
var scalarMs = sw.Elapsed.TotalMilliseconds / iterations;
Console.WriteLine($"标量暴力搜索: {scalarMs:F3} ms/次 (平均 {iterations} 次)");
Console.WriteLine($"SIMD 加速比: {scalarMs / simdMs:F1}x");
Console.WriteLine();

Console.WriteLine($"SIMD Top-{topK}:");
foreach (var (id, score) in simdResult) Console.WriteLine($"  #{id,5} = {score:F6}");

Console.WriteLine();
Console.WriteLine($"标量 Top-{topK}:");
foreach (var (id, score) in scalarResult) Console.WriteLine($"  #{id,5} = {score:F6}");

var maxDiff = simdResult.Zip(scalarResult).Max(x => Math.Abs(x.First.Score - x.Second.Score));
Console.WriteLine();
Console.WriteLine($"结果一致性: maxDiff = {maxDiff:E6} (应 <1E-5)");

Console.WriteLine();
Console.WriteLine($"=== 内存占用估算 ===");
var memBytes = count * dims * sizeof(float);
Console.WriteLine($"向量存储: {count}×{dims}×4B = {memBytes / 1024.0 / 1024.0:F1} MB");
Console.WriteLine($"256维 vs 1536维: 256维内存 = {memBytes / 1024.0 / 1024.0:F1}MB, 1536维 = {count * 1536 * 4 / 1024.0 / 1024.0:F1}MB (降 {(1 - 256.0/1536)*100:F0}%)");

static List<(int Id, float Score)> BruteForceSimd(float[] query, float[][] vectors, int topK) {
    var scores = new (int Id, float Score)[vectors.Length];
    var vecSize = Vector<float>.Count;
    for (var i = 0; i < vectors.Length; i++) {
        var v = vectors[i];
        var dot = 0f;
        var j = 0;
        for (; j <= v.Length - vecSize; j += vecSize) {
            dot += Vector.Dot(new Vector<float>(query, j), new Vector<float>(v, j));
        }
        for (; j < v.Length; j++) dot += query[j] * v[j];
        scores[i] = (i, dot);
    }
    return scores.OrderByDescending(x => x.Score).Take(topK).ToList();
}

static List<(int Id, float Score)> BruteForceScalar(float[] query, float[][] vectors, int topK) {
    var scores = new (int Id, float Score)[vectors.Length];
    for (var i = 0; i < vectors.Length; i++) {
        var v = vectors[i];
        var dot = 0f;
        for (var j = 0; j < v.Length; j++) dot += query[j] * v[j];
        scores[i] = (i, dot);
    }
    return scores.OrderByDescending(x => x.Score).Take(topK).ToList();
}
