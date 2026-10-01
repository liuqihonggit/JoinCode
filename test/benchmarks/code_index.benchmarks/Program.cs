using System.Diagnostics;
using JoinCode.CodeIndex.Vector;
using JoinCode.Abstractions.CodeIndex;

namespace CodeIndexBenchmarks;

public static class Program {

    private static readonly int[] Sizes = [100, 500, 1000, 2000];
    private const int Dimensions = 384;
    private const int TopK = 10;
    private const int SearchIterations = 50;

    public static void Main(string[] args) {
        if (args.Length > 0 && int.TryParse(args[0], out var singleSize)) {
            Console.WriteLine($"=== 单规模压测: {singleSize} ===");
            BenchmarkSize(singleSize);
            BenchmarkLoad(singleSize);
            return;
        }

        Console.WriteLine("=== BruteForce vs HNSW 压测对比 ===");
        Console.WriteLine($"维度: {Dimensions}, TopK: {TopK}, 搜索迭代: {SearchIterations}");
        Console.WriteLine();

        Console.WriteLine($"{"Size",-8} {"BF Build",-12} {"HNSW Build",-12} {"BF Search",-12} {"HNSW Search",-12} {"Recall",-8} {"Graph KB",-10}");
        Console.WriteLine(new string('-', 78));

        foreach (var size in Sizes) {
            BenchmarkSize(size);
        }

        Console.WriteLine();
        Console.WriteLine("=== 图持久化对比 ===");
        Console.WriteLine($"{"Size",-8} {"BF Load",-12} {"HNSW Load",-12} {"Graph Bytes",-12} {"Speedup",-10}");
        Console.WriteLine(new string('-', 58));

        foreach (var size in Sizes) {
            BenchmarkLoad(size);
        }
    }

    private static void BenchmarkSize(int size) {
        var rng = new Random(42);
        var vectors = new (string Id, float[] Vector)[size];
        for (var i = 0; i < size; i++) {
            var v = new float[Dimensions];
            for (var j = 0; j < Dimensions; j++) v[j] = (float)rng.NextDouble();
            vectors[i] = ($"v{i}", v);
        }

        var queries = new float[SearchIterations][];
        for (var q = 0; q < SearchIterations; q++) {
            queries[q] = new float[Dimensions];
            for (var j = 0; j < Dimensions; j++) queries[q][j] = (float)rng.NextDouble();
        }

        var bfSw = Stopwatch.StartNew();
        var bf = new BruteForceAnn();
        bf.AddRange(vectors);
        bfSw.Stop();

        var hnswSw = Stopwatch.StartNew();
        var hnsw = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        hnsw.AddRange(vectors);
        hnswSw.Stop();

        var bfSearchSw = Stopwatch.StartNew();
        var bfResults = new List<IReadOnlyList<(string Id, float Score)>>(SearchIterations);
        for (var q = 0; q < SearchIterations; q++) {
            bfResults.Add(bf.Search(queries[q], TopK, CancellationToken.None));
        }
        bfSearchSw.Stop();

        var hnswSearchSw = Stopwatch.StartNew();
        var hnswResults = new List<IReadOnlyList<(string Id, float Score)>>(SearchIterations);
        for (var q = 0; q < SearchIterations; q++) {
            hnswResults.Add(hnsw.Search(queries[q], TopK, CancellationToken.None));
        }
        hnswSearchSw.Stop();

        var recall = ComputeRecall(bfResults, hnswResults);
        var graphKb = GetGraphBytes(hnsw) / 1024.0;

        Console.WriteLine($"{size,-8} " +
            $"{bfSw.ElapsedMilliseconds + "ms",-12} " +
            $"{hnswSw.ElapsedMilliseconds + "ms",-12} " +
            $"{(bfSearchSw.ElapsedMilliseconds / (double)SearchIterations).ToString("F3") + "ms",-12} " +
            $"{(hnswSearchSw.ElapsedMilliseconds / (double)SearchIterations).ToString("F3") + "ms",-12} " +
            $"{recall.ToString("P1"),-8} " +
            $"{graphKb.ToString("F0") + "KB",-10}");
    }

    private static void BenchmarkLoad(int size) {
        var rng = new Random(42);
        var vectors = new (string Id, float[] Vector)[size];
        for (var i = 0; i < size; i++) {
            var v = new float[Dimensions];
            for (var j = 0; j < Dimensions; j++) v[j] = (float)rng.NextDouble();
            vectors[i] = ($"v{i}", v);
        }

        var bf = new BruteForceAnn();
        bf.AddRange(vectors);

        var hnsw = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        hnsw.AddRange(vectors);

        var vectorDict = new Dictionary<string, float[]>(size);
        for (var i = 0; i < size; i++) vectorDict[vectors[i].Id] = vectors[i].Vector;

        byte[] graphBytes;
        using (var ms = new MemoryStream()) {
            using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            ((IAnnSearchGraphPersistence)hnsw).SaveGraph(bw);
            bw.Flush();
            graphBytes = ms.ToArray();
        }

        var bfLoadSw = Stopwatch.StartNew();
        var bf2 = new BruteForceAnn();
        bf2.AddRange(vectors);
        bfLoadSw.Stop();

        var hnswLoadSw = Stopwatch.StartNew();
        var hnsw2 = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        using (var ms2 = new MemoryStream(graphBytes, writable: false))
        using (var br = new BinaryReader(ms2, System.Text.Encoding.UTF8)) {
            ((IAnnSearchGraphPersistence)hnsw2).LoadGraph(br, vectorDict);
        }
        hnswLoadSw.Stop();

        var speedup = bfLoadSw.ElapsedMilliseconds > 0 && hnswLoadSw.ElapsedMilliseconds > 0
            ? bfLoadSw.ElapsedMilliseconds / (double)hnswLoadSw.ElapsedMilliseconds
            : 0;

        Console.WriteLine($"{size,-8} " +
            $"{bfLoadSw.ElapsedMilliseconds + "ms",-12} " +
            $"{hnswLoadSw.ElapsedMilliseconds + "ms",-12} " +
            $"{graphBytes.Length + "B",-12} " +
            $"{speedup.ToString("F1") + "x",-10}");
    }

    private static double ComputeRecall(
        List<IReadOnlyList<(string Id, float Score)>> bfResults,
        List<IReadOnlyList<(string Id, float Score)>> hnswResults) {
        var total = 0;
        var matched = 0;
        for (var q = 0; q < bfResults.Count; q++) {
            var bfIds = new HashSet<string>(bfResults[q].Select(x => x.Id));
            total += bfIds.Count;
            foreach (var (id, _) in hnswResults[q]) {
                if (bfIds.Contains(id)) matched++;
            }
        }
        return total == 0 ? 0 : matched / (double)total;
    }

    private static long GetGraphBytes(HnswAnn hnsw) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        ((IAnnSearchGraphPersistence)hnsw).SaveGraph(bw);
        bw.Flush();
        return ms.Length;
    }
}
