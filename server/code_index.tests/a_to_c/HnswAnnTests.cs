namespace JoinCode.CodeIndex.Tests;

public sealed class HnswAnnTests {

    private static float[] MakeVector(params float[] values) => values;

    [Fact]
    public void Add_Single_IncreasesCount() {
        var ann = new HnswAnn();
        Assert.Equal(0, ann.Count);

        ann.Add("v1", MakeVector(1f, 0f, 0f));
        Assert.Equal(1, ann.Count);
    }

    [Fact]
    public void Add_DuplicateId_Overwrites() {
        var ann = new HnswAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v1", MakeVector(0f, 1f));

        Assert.Equal(1, ann.Count);
        var results = ann.Search(MakeVector(0f, 1f), 1, CancellationToken.None);
        Assert.Equal("v1", results[0].Id);
    }

    [Fact]
    public void AddRange_BatchAdd() {
        var ann = new HnswAnn();
        ann.AddRange([
            ("v1", MakeVector(1f, 0f)),
            ("v2", MakeVector(0f, 1f)),
            ("v3", MakeVector(1f, 1f))
        ]);

        Assert.Equal(3, ann.Count);
    }

    [Fact]
    public void Remove_DecreasesCount() {
        var ann = new HnswAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        ann.Remove("v1");
        Assert.Equal(1, ann.Count);
    }

    [Fact]
    public void Remove_NonExistent_NoError() {
        var ann = new HnswAnn();
        ann.Remove("nonexistent");
        Assert.Equal(0, ann.Count);
    }

    [Fact]
    public void Search_Empty_ReturnsEmpty() {
        var ann = new HnswAnn();
        var results = ann.Search(MakeVector(1f, 0f), 5, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TopK_ReturnsMostSimilar() {
        var ann = new HnswAnn(m: 4, efConstruction: 16, efSearch: 8);
        ann.Add("v1", MakeVector(1f, 0f, 0f));
        ann.Add("v2", MakeVector(0f, 1f, 0f));
        ann.Add("v3", MakeVector(0.9f, 0.1f, 0f));

        var results = ann.Search(MakeVector(1f, 0f, 0f), 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("v1", results[0].Id);
        Assert.Equal(1f, results[0].Score, 0.01f);
    }

    [Fact]
    public void Search_TopKZero_ReturnsEmpty() {
        var ann = new HnswAnn();
        ann.Add("v1", MakeVector(1f, 0f));

        var results = ann.Search(MakeVector(1f, 0f), 0, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TopKExceedsCount_ReturnsAll() {
        var ann = new HnswAnn(m: 4, efConstruction: 16, efSearch: 8);
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        var results = ann.Search(MakeVector(1f, 0f), 100, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_ResultsSortedDescending() {
        var ann = new HnswAnn(m: 4, efConstruction: 16, efSearch: 8);
        ann.Add("v1", MakeVector(0f, 1f));
        ann.Add("v2", MakeVector(1f, 0f));
        ann.Add("v3", MakeVector(0.7f, 0.7f));

        var results = ann.Search(MakeVector(1f, 0f), 3, CancellationToken.None);

        for (var i = 1; i < results.Count; i++) {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"结果应降序排列: [{i - 1}]={results[i - 1].Score} < [{i}]={results[i].Score}");
        }
    }

    [Fact]
    public void Search_AfterRemove_DoesNotFindRemoved() {
        var ann = new HnswAnn(m: 4, efConstruction: 16, efSearch: 8);
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        ann.Remove("v1");
        var results = ann.Search(MakeVector(1f, 0f), 10, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("v2", results[0].Id);
    }

    [Fact]
    public void Search_LargeDataset_FindsCorrectTopK() {
        var ann = new HnswAnn(m: 8, efConstruction: 64, efSearch: 32);
        var rng = new Random(42);
        for (var i = 0; i < 500; i++) {
            var v = new float[32];
            for (var j = 0; j < 32; j++) v[j] = (float)rng.NextDouble();
            ann.Add($"v{i}", v);
        }

        var query = new float[32];
        for (var j = 0; j < 32; j++) query[j] = (float)rng.NextDouble();

        var results = ann.Search(query, 10, CancellationToken.None);
        Assert.Equal(10, results.Count);

        for (var i = 1; i < results.Count; i++) {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"结果应降序排列: [{i - 1}]={results[i - 1].Score} < [{i}]={results[i].Score}");
        }
    }

    [Fact]
    public async Task SaveGraph_LoadGraph_RestoresSearchResults() {
        var ann1 = new HnswAnn(m: 8, efConstruction: 64, efSearch: 32);
        var vectors = new Dictionary<string, float[]>();
        var rng = new Random(42);
        for (var i = 0; i < 200; i++) {
            var v = new float[16];
            for (var j = 0; j < 16; j++) v[j] = (float)rng.NextDouble();
            ann1.Add($"v{i}", v);
            vectors[$"v{i}"] = v;
        }

        var query = new float[16];
        for (var j = 0; j < 16; j++) query[j] = (float)rng.NextDouble();
        var resultsBefore = ann1.Search(query, 5, CancellationToken.None);

        byte[] graphBytes;
        await using (var ms = new MemoryStream()) {
            await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            ((IAnnSearchGraphPersistence)ann1).SaveGraph(bw);
            bw.Flush();
            graphBytes = ms.ToArray();
        }

        var ann2 = new HnswAnn(m: 8, efConstruction: 64, efSearch: 32);
        await using var ms2 = new MemoryStream(graphBytes, writable: false);
        using var br = new BinaryReader(ms2, System.Text.Encoding.UTF8);
        ((IAnnSearchGraphPersistence)ann2).LoadGraph(br, vectors);

        Assert.Equal(ann1.Count, ann2.Count);

        var resultsAfter = ann2.Search(query, 5, CancellationToken.None);
        Assert.Equal(resultsBefore.Count, resultsAfter.Count);
        for (var i = 0; i < resultsBefore.Count; i++) {
            Assert.Equal(resultsBefore[i].Id, resultsAfter[i].Id);
            Assert.Equal(resultsBefore[i].Score, resultsAfter[i].Score, 0.001f);
        }
    }

    [Fact]
    public async Task LoadGraph_DoesNotReconstructGraph_FastLoad() {
        var ann1 = new HnswAnn(m: 8, efConstruction: 64, efSearch: 32);
        var vectors = new Dictionary<string, float[]>();
        var rng = new Random(42);
        for (var i = 0; i < 1000; i++) {
            var v = new float[16];
            for (var j = 0; j < 16; j++) v[j] = (float)rng.NextDouble();
            ann1.Add($"v{i}", v);
            vectors[$"v{i}"] = v;
        }

        byte[] graphBytes;
        await using (var ms = new MemoryStream()) {
            await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            ((IAnnSearchGraphPersistence)ann1).SaveGraph(bw);
            bw.Flush();
            graphBytes = ms.ToArray();
        }

        var ann2 = new HnswAnn(m: 8, efConstruction: 64, efSearch: 32);
        await using var ms2 = new MemoryStream(graphBytes, writable: false);
        using var br = new BinaryReader(ms2, System.Text.Encoding.UTF8);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        ((IAnnSearchGraphPersistence)ann2).LoadGraph(br, vectors);
        sw.Stop();

        Assert.Equal(1000, ann2.Count);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"LoadGraph 应 <500ms（不重新构图），实际 {sw.ElapsedMilliseconds}ms");
    }
}
