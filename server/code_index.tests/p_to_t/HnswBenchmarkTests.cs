namespace JoinCode.CodeIndex.Tests;

public sealed class HnswBenchmarkTests {

    private static float[] MakeRandomVector(int dims, Random rng) {
        var v = new float[dims];
        for (var j = 0; j < dims; j++) v[j] = (float)rng.NextDouble();
        return v;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task Benchmark_BruteForce_vs_HNSW(int size) {
        const int dims = 384;
        const int topK = 10;
        const int searchIterations = 50;
        var rng = new Random(42);

        var vectors = new (string Id, float[] Vector)[size];
        for (var i = 0; i < size; i++)
            vectors[i] = ($"v{i}", MakeRandomVector(dims, rng));

        var queries = new float[searchIterations][];
        for (var q = 0; q < searchIterations; q++)
            queries[q] = MakeRandomVector(dims, rng);

        var bfSw = System.Diagnostics.Stopwatch.StartNew();
        var bf = new BruteForceAnn();
        bf.AddRange(vectors);
        bfSw.Stop();

        var hnswSw = System.Diagnostics.Stopwatch.StartNew();
        var hnsw = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        hnsw.AddRange(vectors);
        hnswSw.Stop();

        var bfSearchSw = System.Diagnostics.Stopwatch.StartNew();
        var bfResults = new List<IReadOnlyList<(string Id, float Score)>>(searchIterations);
        for (var q = 0; q < searchIterations; q++)
            bfResults.Add(bf.Search(queries[q], topK, CancellationToken.None));
        bfSearchSw.Stop();

        var hnswSearchSw = System.Diagnostics.Stopwatch.StartNew();
        var hnswResults = new List<IReadOnlyList<(string Id, float Score)>>(searchIterations);
        for (var q = 0; q < searchIterations; q++)
            hnswResults.Add(hnsw.Search(queries[q], topK, CancellationToken.None));
        hnswSearchSw.Stop();

        var recall = ComputeRecall(bfResults, hnswResults);

        var graphBytes = await GetGraphBytesAsync(hnsw);

        Assert.True(recall > 0.7, $"召回率过低: {recall:P1}");
        Assert.Equal(size, hnsw.Count);
        Assert.Equal(size, bf.Count);

        var output = $"size={size} | " +
            $"BF构建={bfSw.ElapsedMilliseconds}ms HNSW构建={hnswSw.ElapsedMilliseconds}ms | " +
            $"BF搜索={bfSearchSw.ElapsedMilliseconds / (double)searchIterations:F3}ms " +
            $"HNSW搜索={hnswSearchSw.ElapsedMilliseconds / (double)searchIterations:F3}ms | " +
            $"召回率={recall:P1} | 图={graphBytes / 1024}KB";
        Assert.True(true, output);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task Benchmark_LoadGraph_vs_Reconstruct(int size) {
        const int dims = 384;
        var rng = new Random(42);
        var vectors = new (string Id, float[] Vector)[size];
        for (var i = 0; i < size; i++)
            vectors[i] = ($"v{i}", MakeRandomVector(dims, rng));

        var hnsw = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        hnsw.AddRange(vectors);

        var vectorDict = new Dictionary<string, float[]>(size);
        for (var i = 0; i < size; i++) vectorDict[vectors[i].Id] = vectors[i].Vector;

        byte[] graphBytes;
        await using (var ms = new MemoryStream()) {
            await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            ((IAnnSearchGraphPersistence)hnsw).SaveGraph(bw);
            bw.Flush();
            graphBytes = ms.ToArray();
        }

        var loadSw = System.Diagnostics.Stopwatch.StartNew();
        var hnsw2 = new HnswAnn(m: 16, efConstruction: 200, efSearch: 50);
        await using (var ms2 = new MemoryStream(graphBytes, writable: false)) {
            using var br = new BinaryReader(ms2, System.Text.Encoding.UTF8);
            ((IAnnSearchGraphPersistence)hnsw2).LoadGraph(br, vectorDict);
        }
        loadSw.Stop();

        Assert.Equal(size, hnsw2.Count);
        Assert.True(loadSw.ElapsedMilliseconds < 500,
            $"LoadGraph 应 <500ms，实际 {loadSw.ElapsedMilliseconds}ms");
    }

    private static double ComputeRecall(
        List<IReadOnlyList<(string Id, float Score)>> bfResults,
        List<IReadOnlyList<(string Id, float Score)>> hnswResults) {
        var total = 0;
        var matched = 0;
        for (var q = 0; q < bfResults.Count; q++) {
            var bfIds = new HashSet<string>(bfResults[q].Select(x => x.Id));
            total += bfIds.Count;
            foreach (var (id, _) in hnswResults[q])
                if (bfIds.Contains(id)) matched++;
        }
        return total == 0 ? 0 : matched / (double)total;
    }

    private static async Task<long> GetGraphBytesAsync(HnswAnn hnsw) {
        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        ((IAnnSearchGraphPersistence)hnsw).SaveGraph(bw);
        bw.Flush();
        return ms.Length;
    }
}
