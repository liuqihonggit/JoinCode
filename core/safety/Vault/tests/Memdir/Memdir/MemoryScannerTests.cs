
namespace Core.Tests.Memdir;

public sealed class MemoryScannerTests
{
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();
    private readonly Mock<IMemoryPaths> _pathsMock = new();

    private MemoryScanner CreateSut() => new(_fs, _pathsMock.Object);

    private static string MemoryJson(MemoryEntry entry)
    {
        return JsonSerializer.Serialize(entry, MemdirJsonContext.Default.MemoryEntry);
    }

    [Fact]
    public async Task ScanDirectoryAsync_NonExistentDirectory_ReturnsEmpty()
    {
        var sut = CreateSut();

        var result = await sut.ScanDirectoryAsync("/does/not/exist").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanDirectoryAsync_ValidJsonFiles_ReturnsMemories()
    {
        var sut = CreateSut();
        var entry = MemoryEntry.Create(MemoryType.User, "hello world", now: DateTime.UtcNow) with { Id = "abc" };
        _fs.WriteAllText("/mem/user/abc.json", MemoryJson(entry));

        var result = await sut.ScanDirectoryAsync("/mem/user").ConfigureAwait(true);

        result.Should().ContainSingle(m => m.Id == "abc" && m.Type == MemoryType.User && m.Content == "hello world");
    }

    [Fact]
    public async Task ScanDirectoryAsync_InvalidJson_IsIgnoredAndReturnsEmpty()
    {
        var sut = CreateSut();
        _fs.WriteAllText("/mem/user/bad.json", "not json");

        var result = await sut.ScanDirectoryAsync("/mem/user").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanDirectoryAsync_IdMismatch_ReturnsMemory()
    {
        var sut = CreateSut();
        var entry = MemoryEntry.Create(MemoryType.User, "content", now: DateTime.UtcNow) with { Id = "realid" };
        _fs.WriteAllText("/mem/user/wrongname.json", MemoryJson(entry));

        var result = await sut.ScanDirectoryAsync("/mem/user").ConfigureAwait(true);

        result.Should().ContainSingle(m => m.Id == "realid");
    }

    [Fact]
    public async Task ScanByTypeAsync_UsesMemoryPathsDirectory()
    {
        var sut = CreateSut();
        _pathsMock.Setup(p => p.GetMemoryDirectoryByType(MemoryType.Project, null)).Returns("/mem/project");
        var entry = MemoryEntry.Create(MemoryType.Project, "project memory", now: DateTime.UtcNow) with { Id = "p1" };
        _fs.WriteAllText("/mem/project/p1.json", MemoryJson(entry));

        var result = await sut.ScanByTypeAsync(MemoryType.Project).ConfigureAwait(true);

        result.Should().ContainSingle(m => m.Type == MemoryType.Project);
    }

    [Fact]
    public async Task ScanAllAsync_ScansAllTypes()
    {
        var sut = CreateSut();
        _pathsMock.Setup(p => p.GetMemoryDirectoryByType(It.IsAny<MemoryType>(), null))
            .Returns<MemoryType, string?>((type, _) => $"/mem/{type.ToString().ToLowerInvariant()}");
        var userEntry = MemoryEntry.Create(MemoryType.User, "user memory", now: DateTime.UtcNow) with { Id = "u1" };
        var refEntry = MemoryEntry.Create(MemoryType.Reference, "reference memory", now: DateTime.UtcNow) with { Id = "r1" };
        _fs.WriteAllText("/mem/user/u1.json", MemoryJson(userEntry));
        _fs.WriteAllText("/mem/reference/r1.json", MemoryJson(refEntry));

        var result = await sut.ScanAllAsync().ConfigureAwait(true);

        result.Should().HaveCount(2);
        result.Should().Contain(m => m.Type == MemoryType.User);
        result.Should().Contain(m => m.Type == MemoryType.Reference);
    }

    [Fact]
    public void BuildIndex_GroupsByTypeTagAndSource()
    {
        var sut = CreateSut();
        var memories = new List<MemoryEntry>
        {
            MemoryEntry.Create(MemoryType.User, "A", title: "A", tags: new[] { "tag1", "tag2" }, source: "src1"),
            MemoryEntry.Create(MemoryType.User, "B", title: "B", tags: new[] { "tag2", "tag3" }),
            MemoryEntry.Create(MemoryType.Project, "C", title: "C", source: "src1")
        };

        var index = sut.BuildIndex(memories);

        index.ByType.Should().ContainKey(MemoryType.User).WhoseValue.Should().HaveCount(2);
        index.ByType.Should().ContainKey(MemoryType.Project).WhoseValue.Should().HaveCount(1);
        index.ByTag.Should().ContainKey("tag2").WhoseValue.Should().HaveCount(2);
        index.BySource.Should().ContainKey("src1").WhoseValue.Should().HaveCount(2);
    }

    [Fact]
    public void BuildIndex_FindByType_ReturnsReadOnlyList()
    {
        var sut = CreateSut();
        var memories = new List<MemoryEntry>
        {
            MemoryEntry.Create(MemoryType.Feedback, "feedback memory")
        };

        var index = sut.BuildIndex(memories);

        index.FindByType(MemoryType.Feedback).Should().ContainSingle();
        index.FindByType(MemoryType.User).Should().BeEmpty();
    }

    [Fact]
    public void BuildIndex_FindByTag_ReturnsReadOnlyList()
    {
        var sut = CreateSut();
        var memories = new List<MemoryEntry>
        {
            MemoryEntry.Create(MemoryType.User, "tagged", tags: new[] { "important" })
        };

        var index = sut.BuildIndex(memories);

        index.FindByTag("important").Should().ContainSingle();
        index.FindByTag("missing").Should().BeEmpty();
    }

    [Fact]
    public void BuildIndex_FindBySource_ReturnsReadOnlyList()
    {
        var sut = CreateSut();
        var memories = new List<MemoryEntry>
        {
            MemoryEntry.Create(MemoryType.User, "sourced", source: "srcA")
        };

        var index = sut.BuildIndex(memories);

        index.FindBySource("srcA").Should().ContainSingle();
        index.FindBySource("srcB").Should().BeEmpty();
    }
}
