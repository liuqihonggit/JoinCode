
namespace Core.Tests.Memdir;

public sealed class MemoryRelevanceSelectorTests
{
    private readonly FakeClockService _clock = new();
    private readonly Mock<IMemoryAgeCalculator> _ageCalculatorMock = new();

    private MemoryRelevanceSelector CreateSut()
        => new(_ageCalculatorMock.Object, clock: _clock);

    private static MemoryEntry Make(string content, MemoryType type = MemoryType.User, string? title = null, IEnumerable<string>? tags = null, int accessCount = 0, TimeSpan? ttl = null)
        => MemoryEntry.Create(type, content, title: title, tags: tags, ttl: ttl) with { AccessCount = accessCount };

    [Fact]
    public async Task SelectRelevantMemoriesAsync_EmptyMemories_ReturnsEmpty()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);

        var result = await sut.SelectRelevantMemoriesAsync(Array.Empty<MemoryEntry>(), "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Archived_IsExcluded()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memory = Make("query match").WithArchived(_clock.GetUtcNow());

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Expired_IsExcluded()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var now = _clock.GetUtcNow();
        var memory = Make("query match") with { ExpiresAt = now.AddSeconds(-1) };

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_ContentMatch_ReturnsScoredMemory()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memory = Make("the quick brown fox");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "quick fox").ConfigureAwait(true);

        result.Should().ContainSingle();
        result[0].Memory.Id.Should().Be(memory.Id);
        result[0].RelevanceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_NoMatch_ReturnsEmpty()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.0);
        var memory = Make("unrelated content");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "xyz123").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_TagMatch_BoostsScore()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var withoutTag = Make("some content");
        var withTag = Make("some content", tags: new[] { "queryword" });

        var result = await sut.SelectRelevantMemoriesAsync(new[] { withoutTag, withTag }, "queryword").ConfigureAwait(true);

        result.Should().HaveCount(2);
        result[0].Memory.Id.Should().Be(withTag.Id);
        result[0].RelevanceScore.Should().BeGreaterThan(result[1].RelevanceScore);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_TitleMatch_BoostsScore()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var withoutTitle = Make("content");
        var withTitle = Make("content", title: "queryword title");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { withoutTitle, withTitle }, "queryword").ConfigureAwait(true);

        result.Should().HaveCount(2);
        result[0].Memory.Id.Should().Be(withTitle.Id);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_TypeWeight_AffectsOrdering()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var reference = Make("important query", MemoryType.Reference);
        var user = Make("important query", MemoryType.User);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { reference, user }, "important query").ConfigureAwait(true);

        result.Should().HaveCount(2);
        result[0].Memory.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_AccessCount_BoostsScore()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var lowAccess = Make("query match", accessCount: 0);
        var highAccess = Make("query match", accessCount: 100);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { lowAccess, highAccess }, "query match").ConfigureAwait(true);

        result[0].Memory.Id.Should().Be(highAccess.Id);
        result[0].RelevanceScore.Should().BeGreaterThan(result[1].RelevanceScore);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_MaxResults_IsRespected()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memories = Enumerable.Range(0, 10).Select(i => Make($"query {i}")).ToList();

        var result = await sut.SelectRelevantMemoriesAsync(memories, "query", maxResults: 3).ConfigureAwait(true);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Score_IsCappedAtOne()
    {
        var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(1.0);
        var memory = Make("query match", MemoryType.User, title: "query match", tags: new[] { "query" }, accessCount: 1000);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query match").ConfigureAwait(true);

        result.Should().ContainSingle();
        result[0].RelevanceScore.Should().BeLessThanOrEqualTo(1.0);
    }
}
