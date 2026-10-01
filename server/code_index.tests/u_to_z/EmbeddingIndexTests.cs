namespace JoinCode.CodeIndex.Tests;

public sealed class EmbeddingIndexTests {

    private static EmbeddingIndex CreateIndex(int dims = 8) {
        var embed = new FakeEmbeddingModel(dims);
        var ann = new BruteForceAnn();
        return new EmbeddingIndex(embed, ann, TestFileSystem.Current);
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
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

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
        var results = await index.SearchAsync("Bar", 1, CancellationToken.None,
            new SearchOptions { IncludeParentDocument = true });

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
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = "missing-parent" };
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_ChunkWithoutParent_ParentDocumentTextIsNull() {
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = null };
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_IncludeSourceText_ReturnsSourceText() {
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk(text: "void Bar() { }");
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Bar", 1, CancellationToken.None,
            new SearchOptions { IncludeSourceText = true });
        var result = Assert.Single(results);
        Assert.NotNull(result.SourceText);
        Assert.Contains("Bar", result.SourceText);
    }

    [Fact]
    public async Task SearchAsync_DefaultOptions_SourceTextIsNull() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([CreateChunk()], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.SourceText);
    }

    [Fact]
    public async Task SearchAsync_DefaultOptions_ParentDocumentTextIsNull() {
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = "class-001" };
        parentStore.Add(new ParentDocument {
            ChunkId = "class-001",
            FilePath = "test.cs",
            SymbolFqn = "Test.Foo",
            StartLine = 1,
            EndLine = 10,
            SourceText = "class Foo { }"
        });
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_IncludeParentDocumentFalse_ParentDocumentTextIsNull() {
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk() with { ParentChunkId = "class-001" };
        parentStore.Add(new ParentDocument {
            ChunkId = "class-001",
            FilePath = "test.cs",
            SymbolFqn = "Test.Foo",
            StartLine = 1,
            EndLine = 10,
            SourceText = "class Foo { }"
        });
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 1, CancellationToken.None,
            new SearchOptions { IncludeParentDocument = false });
        var result = Assert.Single(results);
        Assert.Null(result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_BothOptionsTrue_ReturnsBothSourceTextAndParent() {
        using var parentStore = new InMemoryParentDocumentStore(TestFileSystem.Current);
        var embed = new FakeEmbeddingModel(8);
        var ann = new BruteForceAnn();
        await using var index = new EmbeddingIndex(embed, ann, TestFileSystem.Current, parentStore);

        var chunk = CreateChunk(text: "void Bar() { }") with { ParentChunkId = "class-001" };
        parentStore.Add(new ParentDocument {
            ChunkId = "class-001",
            FilePath = "test.cs",
            SymbolFqn = "Test.Foo",
            StartLine = 1,
            EndLine = 10,
            SourceText = "class Foo { void Bar() { } }"
        });
        await index.IndexChunksAsync([chunk], CancellationToken.None);

        var results = await index.SearchAsync("Bar", 1, CancellationToken.None,
            new SearchOptions { IncludeSourceText = true, IncludeParentDocument = true });
        var result = Assert.Single(results);
        Assert.NotNull(result.SourceText);
        Assert.Contains("Bar", result.SourceText);
        Assert.NotNull(result.ParentDocumentText);
        Assert.Contains("class Foo", result.ParentDocumentText);
    }

    [Fact]
    public async Task SearchAsync_FileTypeCs_FiltersOutMd() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", file: "src.cs", text: "void Foo() {}"),
            CreateChunk("m1", "Doc.Guide", file: "guide.md", text: "# Guide\nFoo usage.")
        ], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 10, CancellationToken.None,
            new SearchOptions { FileType = "cs" });
        Assert.All(results, r => Assert.EndsWith(".cs", r.FilePath));
    }

    [Fact]
    public async Task SearchAsync_FileTypeMd_FiltersOutCs() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", file: "src.cs", text: "void Foo() {}"),
            CreateChunk("m1", "Doc.Guide", file: "guide.md", text: "# Guide\nFoo usage.")
        ], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 10, CancellationToken.None,
            new SearchOptions { FileType = "md" });
        Assert.All(results, r => Assert.EndsWith(".md", r.FilePath));
    }

    [Fact]
    public async Task SearchAsync_FileTypeNull_ReturnsAll() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", file: "src.cs", text: "void Foo() {}"),
            CreateChunk("m1", "Doc.Guide", file: "guide.md", text: "# Guide\nFoo usage.")
        ], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 10, CancellationToken.None);
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task SearchAsync_FileTypeCs_NoMdResults() {
        await using var index = CreateIndex();
        await index.IndexChunksAsync([
            CreateChunk("c1", "Test.Foo", file: "src.cs", text: "void Foo() {}"),
            CreateChunk("m1", "Doc.Guide", file: "guide.md", text: "# Guide\nFoo usage.")
        ], CancellationToken.None);

        var results = await index.SearchAsync("Foo", 10, CancellationToken.None,
            new SearchOptions { FileType = "cs" });
        Assert.DoesNotContain(results, r => r.FilePath.EndsWith(".md"));
    }

    [Fact]
    public async Task SaveLoadV5_RoundTrip_PreservesData() {
        await using var index = CreateIndex();
        var chunks = new List<ChunkInfo> {
            CreateChunk("c1", "Test.A", file: "a.cs", start: 1, end: 10, hash: "h1", text: "void A() {}"),
            CreateChunk("c2", "Test.B", file: "b.cs", start: 5, end: 20, hash: "h2", text: "void B() {}"),
            CreateChunk("c3", "Test.C", file: "c.cs", start: 1, end: 5, hash: "h3", text: "void C() {}")
        };
        await index.IndexChunksAsync(chunks, CancellationToken.None);
        Assert.Equal(3, index.ChunkCount);

        var dir = Path.Combine(TestFileSystem.Current.GetCurrentDirectory(), "test_v5_idx");
        await index.SaveAsyncV5(dir, CancellationToken.None);

        await using var index2 = CreateIndex();
        var loaded = await index2.LoadAsyncV5(dir, CancellationToken.None);
        Assert.True(loaded);
        Assert.Equal(3, index2.ChunkCount);
        Assert.Equal(IndexStatus.Ready, index2.Status);
    }

    [Fact]
    public async Task SaveLoadV5_LargeDataset_PreservesCount() {
        await using var index = CreateIndex();
        var chunks = new List<ChunkInfo>();
        for (var i = 0; i < 300; i++) {
            chunks.Add(CreateChunk($"c{i}", $"Test.N{i}", file: $"f{i}.cs", start: i, end: i + 10, hash: $"h{i}", text: $"void N{i}() {{}}"));
        }
        await index.IndexChunksAsync(chunks, CancellationToken.None);
        Assert.Equal(300, index.ChunkCount);

        var dir = Path.Combine(TestFileSystem.Current.GetCurrentDirectory(), "test_v5_large");
        await index.SaveAsyncV5(dir, CancellationToken.None);

        await using var index2 = CreateIndex();
        var loaded = await index2.LoadAsyncV5(dir, CancellationToken.None);
        Assert.True(loaded);
        Assert.Equal(300, index2.ChunkCount);
    }

    [Fact]
    public async Task LoadV5_FileNotExists_ReturnsFalse() {
        await using var index = CreateIndex();
        var loaded = await index.LoadAsyncV5("nonexistent_dir_v5", CancellationToken.None);
        Assert.False(loaded);
    }

    [Fact]
    public async Task SaveLoadV6_RoundTrip_PreservesData() {
        TestFileSystem.UseRealFileSystem = true;
        try {
            await using var index = CreateIndex();
            var chunks = new List<ChunkInfo> {
                CreateChunk("c1", "Test.A", file: "a.cs", start: 1, end: 10, hash: "h1", text: "void A() {}"),
                CreateChunk("c2", "Test.B", file: "b.cs", start: 5, end: 20, hash: "h2", text: "void B() {}"),
                CreateChunk("c3", "Test.C", file: "c.cs", start: 1, end: 5, hash: "h3", text: "void C() {}")
            };
            await index.IndexChunksAsync(chunks, CancellationToken.None);
            Assert.Equal(3, index.ChunkCount);

            var dir = Path.Combine(Path.GetTempPath(), $"test_v6_idx_{Guid.NewGuid():N}");
            await index.SaveAsyncV6(dir, CancellationToken.None);

            await using var index2 = CreateIndex();
            var loaded = await index2.LoadAsyncV6(dir, CancellationToken.None);
            Assert.True(loaded);
            Assert.Equal(3, index2.ChunkCount);
            Assert.Equal(IndexStatus.Ready, index2.Status);
        }
        finally {
            TestFileSystem.UseRealFileSystem = false;
        }
    }

    [Fact]
    public async Task SaveLoadV6_LargeDataset_PreservesCount() {
        TestFileSystem.UseRealFileSystem = true;
        try {
            await using var index = CreateIndex();
            var chunks = new List<ChunkInfo>();
            for (var i = 0; i < 300; i++) {
                chunks.Add(CreateChunk($"c{i}", $"Test.N{i}", file: $"f{i}.cs", start: i, end: i + 10, hash: $"h{i}", text: $"void N{i}() {{}}"));
            }
            await index.IndexChunksAsync(chunks, CancellationToken.None);
            Assert.Equal(300, index.ChunkCount);

            var dir = Path.Combine(Path.GetTempPath(), $"test_v6_large_{Guid.NewGuid():N}");
            await index.SaveAsyncV6(dir, CancellationToken.None);

            await using var index2 = CreateIndex();
            var loaded = await index2.LoadAsyncV6(dir, CancellationToken.None);
            Assert.True(loaded);
            Assert.Equal(300, index2.ChunkCount);
        }
        finally {
            TestFileSystem.UseRealFileSystem = false;
        }
    }

    [Fact]
    public async Task LoadV6_FileNotExists_ReturnsFalse() {
        await using var index = CreateIndex();
        var loaded = await index.LoadAsyncV6("nonexistent_dir_v6", CancellationToken.None);
        Assert.False(loaded);
    }
}
