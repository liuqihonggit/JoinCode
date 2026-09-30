namespace JoinCode.CodeIndex.Tests;

public sealed class EmbeddingIndexTests {

    private static EmbeddingIndex CreateIndex(int dims = 8) {
        var embed = new FakeEmbeddingModel(dims);
        var ann = new BruteForceAnn();
        return new EmbeddingIndex(embed, ann);
    }

    private static ChunkInfo CreateChunk(
        string id = "c1",
        string fqn = "Test.Foo",
        string file = "test.cs",
        int start = 1, int end = 10,
        string hash = "h1",
        string text = "void Foo() {}") => new() {
        ChunkId = id,
        SymbolFqn = fqn,
        Kind = SymbolKind.Method,
        FilePath = file,
        StartLine = start,
        EndLine = end,
        LanguageId = "c-sharp",
        ContentHash = hash,
        SourceText = text
    };

    [Fact]
    public async Task Constructor_InitialState_NotReady() {
        await using var index = CreateIndex();
        Assert.Equal(IndexStatus.NotReady, index.Status);
        Assert.Equal(0, index.ChunkCount);
    }

    [Fact]
    public async Task IndexChunksAsync_EmptyList_NoChange() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([], CancellationToken.None);
        Assert.Equal(IndexStatus.NotReady, index.Status);
    }

    [Fact]
    public async Task IndexChunksAsync_SingleChunk_StatusReady() {
        await using var index = CreateIndex();
        var chunk = CreateChunk();
        await index.IndexChunksAsync([chunk], CancellationToken.None);
        Assert.Equal(IndexStatus.Ready, index.Status);
        Assert.Equal(1, index.ChunkCount);
    }

    [Fact]
    public async Task IndexChunksAsync_MultipleChunks_AllIndexed() {
        await using var index = CreateIndex();
        var chunks = new List<ChunkInfo> {
            CreateChunk("c1", "Test.A"),
            CreateChunk("c2", "Test.B"),
            CreateChunk("c3", "Test.C")
        };
        await index.IndexChunksAsync(chunks, CancellationToken.None);
        Assert.Equal(IndexStatus.Ready, index.Status);
        Assert.Equal(3, index.ChunkCount);
    }

    [Fact]
    public async Task IndexChunksAsync_UnchangedHash_SkipsEmbedding() {
        await using var index = CreateIndex();
        var chunk = CreateChunk("c1", "Test.Foo", hash: "h1");
        await index.IndexChunksAsync([chunk], CancellationToken.None);
        Assert.Equal(1, index.ChunkCount);

        var chunk2 = CreateChunk("c1", "Test.Foo", hash: "h1", text: "DIFFERENT TEXT");
        await index.IndexChunksAsync([chunk2], CancellationToken.None);
        Assert.Equal(1, index.ChunkCount);
        Assert.Equal(IndexStatus.Ready, index.Status);
    }

    [Fact]
    public async Task IndexChunksAsync_ChangedHash_ReEmbeds() {
        await using var index = CreateIndex();
        var chunk1 = CreateChunk("c1", "Test.Foo", hash: "h1");
        await index.IndexChunksAsync([chunk1], CancellationToken.None);

        var chunk2 = CreateChunk("c1", "Test.Foo", hash: "h2");
        await index.IndexChunksAsync([chunk2], CancellationToken.None);
        Assert.Equal(1, index.ChunkCount);
        Assert.Equal(IndexStatus.Ready, index.Status);
    }

    [Fact]
    public async Task SearchAsync_NotReady_ReturnsEmpty() {
        await using var index = CreateIndex();
        var results = await index.SearchAsync("query", 5, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_AfterIndexing_ReturnsResults() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", text: "void Foo() {}"),
            CreateChunk("c2", "Test.Bar", text: "void Bar() {}")
        ], CancellationToken.None);

        var results = await index.SearchAsync("void Foo() {}", 2, CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.Equal("c1", results[0].ChunkId);
    }

    [Fact]
    public async Task SearchAsync_TopK_LimitsResults() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.A"),
            CreateChunk("c2", "Test.B"),
            CreateChunk("c3", "Test.C")
        ], CancellationToken.None);

        var results = await index.SearchAsync("query", 2, CancellationToken.None);
        Assert.True(results.Count <= 2);
    }

    [Fact]
    public async Task SearchAsync_TopKZero_ReturnsEmpty() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([CreateChunk()], CancellationToken.None);
        var results = await index.SearchAsync("query", 0, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ResultContainsMetadata() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", file: "src.cs", start: 5, end: 15)
        ], CancellationToken.None);

        var results = await index.SearchAsync("query", 1, CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("src.cs", results[0].FilePath);
        Assert.Equal("Test.Foo", results[0].SymbolFqn);
        Assert.Equal(5, results[0].StartLine);
        Assert.Equal(15, results[0].EndLine);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesAllChunksForFile() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.A", file: "a.cs"),
            CreateChunk("c2", "Test.B", file: "a.cs"),
            CreateChunk("c3", "Test.C", file: "b.cs")
        ], CancellationToken.None);
        Assert.Equal(3, index.ChunkCount);

        await index.RemoveFileAsync("a.cs", CancellationToken.None);
        Assert.Equal(1, index.ChunkCount);

        var results = await index.SearchAsync("query", 10, CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("b.cs", results[0].FilePath);
    }

    [Fact]
    public async Task RemoveFileAsync_NonExistentFile_NoError() {
        await using var index = CreateIndex();
        await index.RemoveFileAsync("nonexistent.cs", CancellationToken.None);
        Assert.Equal(0, index.ChunkCount);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesAllChunks_StatusBecomesNotReady() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.A", file: "a.cs")
        ], CancellationToken.None);
        Assert.Equal(IndexStatus.Ready, index.Status);

        await index.RemoveFileAsync("a.cs", CancellationToken.None);
        Assert.Equal(IndexStatus.NotReady, index.Status);
    }

    [Fact]
    public async Task Clear_ResetsToNotReady() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.A"),
            CreateChunk("c2", "Test.B")
        ], CancellationToken.None);
        Assert.Equal(2, index.ChunkCount);

        index.Clear();
        Assert.Equal(0, index.ChunkCount);
        Assert.Equal(IndexStatus.NotReady, index.Status);
    }

    [Fact]
    public async Task SearchAsync_AfterClear_ReturnsEmpty() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([CreateChunk()], CancellationToken.None);
        index.Clear();

        var results = await index.SearchAsync("query", 5, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task IndexChunksAsync_Cancellation_Throws() {
        await using var index = CreateIndex();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            index.IndexChunksAsync([CreateChunk()], cts.Token));
    }

    [Fact]
    public async Task Dispose_CalledTwice_NoError() {
        var index = CreateIndex();
        await index.DisposeAsync();
        await index.DisposeAsync();
    }

    [Fact]
    public async Task SearchAsync_WithParentStore_ReturnsParentDocumentText() {
        using var parentStore = new InMemoryParentDocumentStore();
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, parentStore);

        var methodChunk = CreateChunk(id: "m1", text: "void Bar() { }") with {
            ParentChunkId = "class-001"
        };
        parentStore.Add(new ParentDocument {
            ChunkId = "class-001",
            FilePath = "test.cs",
            SymbolFqn = "Test.Foo",
            StartLine = 1,
            EndLine = 10,
            SourceText = "class Foo { void Bar() { } }"
        });

        await index.IndexChunksAsync([methodChunk], CancellationToken.None);
        var results = await index.SearchAsync("Bar", 1, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.NotNull(result.ParentDocumentText);
        Assert.Contains("class Foo", result.ParentDocumentText);
        Assert.Equal("Test.Foo", result.ParentSymbolFqn);
        Assert.Equal(1, result.ParentStartLine);
        Assert.Equal(10, result.ParentEndLine);
    }

    [Fact]
    public async Task SearchAsync_WithoutParentStore_ParentDocumentTextIsNull() {
        await using var index = CreateIndex();
        var chunk = CreateChunk() with { ParentChunkId = "parent-001" };

        await index.IndexChunksAsync([chunk], CancellationToken.None);
        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_ParentStoreMissingDoc_ParentDocumentTextIsNull() {
        using var parentStore = new InMemoryParentDocumentStore();
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = "missing-parent" };
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_ChunkWithoutParent_ParentDocumentTextIsNull() {
        using var parentStore = new InMemoryParentDocumentStore();
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = null };
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }
}
