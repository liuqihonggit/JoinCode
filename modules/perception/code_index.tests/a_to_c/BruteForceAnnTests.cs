namespace JoinCode.CodeIndex.Tests;

public sealed class BruteForceAnnTests {

    private static float[] MakeVector(params float[] values) => values;

    [Fact]
    public void Add_Single_IncreasesCount() {
        var ann = new BruteForceAnn();
        Assert.Equal(0, ann.Count);

        ann.Add("v1", MakeVector(1f, 0f, 0f));
        Assert.Equal(1, ann.Count);
    }

    [Fact]
    public void Add_DuplicateId_Overwrites() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v1", MakeVector(0f, 1f));

        Assert.Equal(1, ann.Count);
        var results = ann.Search(MakeVector(0f, 1f), 1, CancellationToken.None);
        Assert.Equal("v1", results[0].Id);
        Assert.Equal(1f, results[0].Score, 0.0001f);
    }

    [Fact]
    public void AddRange_BatchAdd() {
        var ann = new BruteForceAnn();
        ann.AddRange([
            ("v1", MakeVector(1f, 0f)),
            ("v2", MakeVector(0f, 1f)),
            ("v3", MakeVector(1f, 1f))
        ]);

        Assert.Equal(3, ann.Count);
    }

    [Fact]
    public void Remove_DecreasesCount() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        ann.Remove("v1");
        Assert.Equal(1, ann.Count);
    }

    [Fact]
    public void Remove_NonExistent_NoError() {
        var ann = new BruteForceAnn();
        ann.Remove("nonexistent");
        Assert.Equal(0, ann.Count);
    }

    [Fact]
    public void Search_Empty_ReturnsEmpty() {
        var ann = new BruteForceAnn();
        var results = ann.Search(MakeVector(1f, 0f), 5, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TopK_ReturnsMostSimilar() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f, 0f));
        ann.Add("v2", MakeVector(0f, 1f, 0f));
        ann.Add("v3", MakeVector(0.9f, 0.1f, 0f));

        var results = ann.Search(MakeVector(1f, 0f, 0f), 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("v1", results[0].Id);
        Assert.Equal(1f, results[0].Score, 0.0001f);
        Assert.Equal("v3", results[1].Id);
    }

    [Fact]
    public void Search_TopKZero_ReturnsEmpty() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f));

        var results = ann.Search(MakeVector(1f, 0f), 0, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TopKExceedsCount_ReturnsAll() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        var results = ann.Search(MakeVector(1f, 0f), 100, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_ResultsSortedDescending() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(0f, 1f));
        ann.Add("v2", MakeVector(1f, 0f));
        ann.Add("v3", MakeVector(0.7f, 0.7f));

        var results = ann.Search(MakeVector(1f, 0f), 3, CancellationToken.None);

        for (var i = 1; i < results.Count; i++) {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"结果应降序排列: [{i-1}]={results[i-1].Score} < [{i}]={results[i].Score}");
        }
    }

    [Fact]
    public void Search_CancellationRequested_Throws() {
        var ann = new BruteForceAnn();
        for (var i = 0; i < 1000; i++) {
            ann.Add($"v{i}", MakeVector(1f, 0f, 0f));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ann.Search(MakeVector(1f, 0f, 0f), 10, cts.Token));
    }

    [Fact]
    public void Search_AfterRemove_DoesNotFindRemoved() {
        var ann = new BruteForceAnn();
        ann.Add("v1", MakeVector(1f, 0f));
        ann.Add("v2", MakeVector(0f, 1f));

        ann.Remove("v1");
        var results = ann.Search(MakeVector(1f, 0f), 10, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("v2", results[0].Id);
    }
}
