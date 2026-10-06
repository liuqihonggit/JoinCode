
namespace Core.Tests.Memdir;

public sealed class MemoryRelevanceSelectorTests {
    private readonly FakeClockService _clock = new();
    private readonly Mock<IMemoryAgeCalculator> _ageCalculatorMock = new();

    private MemoryRelevanceSelector CreateSut()
        => new(_ageCalculatorMock.Object, clock: _clock);

    private static MemoryEntry Make(string content, MemoryType type = MemoryType.User, string? title = null, IEnumerable<string>? tags = null, int accessCount = 0, TimeSpan? ttl = null)
        => MemoryEntry.Create(type, content, title: title, tags: tags, ttl: ttl) with { AccessCount = accessCount };

    [Fact]
    public async Task SelectRelevantMemoriesAsync_EmptyMemories_ReturnsEmpty() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);

        var result = await sut.SelectRelevantMemoriesAsync(Array.Empty<MemoryEntry>(), "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Archived_IsExcluded() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memory = Make("query match").WithArchived(_clock.GetUtcNow());

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Expired_IsExcluded() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var now = _clock.GetUtcNow();
        var memory = Make("query match") with { ExpiresAt = now.AddSeconds(-1) };

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_ContentMatch_ReturnsScoredMemory() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memory = Make("the quick brown fox");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "quick fox").ConfigureAwait(true);

        result.Should().ContainSingle();
        result[0].Memory.Id.Should().Be(memory.Id);
        result[0].RelevanceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_NoMatch_ReturnsEmpty() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.0);
        var memory = Make("unrelated content");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "xyz123").ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_TagMatch_BoostsScore() {
        using var sut = CreateSut();
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
    public async Task SelectRelevantMemoriesAsync_TitleMatch_BoostsScore() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var withoutTitle = Make("content");
        var withTitle = Make("content", title: "queryword title");

        var result = await sut.SelectRelevantMemoriesAsync(new[] { withoutTitle, withTitle }, "queryword").ConfigureAwait(true);

        result.Should().HaveCount(2);
        result[0].Memory.Id.Should().Be(withTitle.Id);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_TypeWeight_AffectsOrdering() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var reference = Make("important query", MemoryType.Reference);
        var user = Make("important query", MemoryType.User);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { reference, user }, "important query").ConfigureAwait(true);

        result.Should().HaveCount(2);
        result[0].Memory.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_AccessCount_BoostsScore() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var lowAccess = Make("query match", accessCount: 0);
        var highAccess = Make("query match", accessCount: 100);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { lowAccess, highAccess }, "query match").ConfigureAwait(true);

        result[0].Memory.Id.Should().Be(highAccess.Id);
        result[0].RelevanceScore.Should().BeGreaterThan(result[1].RelevanceScore);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_MaxResults_IsRespected() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        var memories = Enumerable.Range(0, 10).Select(i => Make($"query {i}")).ToList();

        var result = await sut.SelectRelevantMemoriesAsync(memories, "query", maxResults: 3).ConfigureAwait(true);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectRelevantMemoriesAsync_Score_IsCappedAtOne() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(1.0);
        var memory = Make("query match", MemoryType.User, title: "query match", tags: new[] { "query" }, accessCount: 1000);

        var result = await sut.SelectRelevantMemoriesAsync(new[] { memory }, "query match").ConfigureAwait(true);

        result.Should().ContainSingle();
        result[0].RelevanceScore.Should().BeLessThanOrEqualTo(1.0);
    }

    // === ScoreMemory internal 直接测试: 6 加权因子独立验证 ===

    private static (HashSet<string> words, AhoCorasick<bool> ac) BuildQueryArgs(string query) {
        var words = QueryWordHelper.ExtractWords(query, minLength: 2);
        var ac = AhoCorasick.CreateBool(words, ignoreCase: true);
        return (words, ac);
    }

    [Fact]
    public void ScoreMemory_KeywordMatch_ContributesZeroPointFourWeight() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("database optimization");
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // matchingWords=2, queryWords.Count=2 → 2/2*0.4=0.4
        // type=User weight=1.0 → 0.4
        // agedScore=0 → 0.4*0.5 + 0*0.5 = 0.2
        // accessCount=0 → ×1.0 = 0.2
        scored.RelevanceScore.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void ScoreMemory_PartialKeywordMatch_ContributesRatio() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("database content");
        var (words, ac) = BuildQueryArgs("database network");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // matchingWords=1 (只匹配 database), queryWords.Count=2 → 1/2*0.4=0.2
        // type=User → 0.2, agedScore=0 → 0.1
        scored.RelevanceScore.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void ScoreMemory_TagMatch_ContributesZeroPointOneFivePerTag() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("unrelated", tags: new[] { "database" });
        var (words, ac) = BuildQueryArgs("database");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // keywordScore=0, tagMatches=1 → +0.15, type=User → 0.15, agedScore=0 → 0.075
        scored.RelevanceScore.Should().BeApproximately(0.075, 1e-9);
    }

    [Fact]
    public void ScoreMemory_MultipleTagMatches_ContributesZeroPointOneFiveEach() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("unrelated", tags: new[] { "database", "optimization" });
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // tagMatches=2 → +0.3, type=User → 0.3, agedScore=0 → 0.15
        scored.RelevanceScore.Should().BeApproximately(0.15, 1e-9);
    }

    [Fact]
    public void ScoreMemory_TitleMatch_ContributesZeroPointTwoWeight() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("unrelated", title: "database");
        var (words, ac) = BuildQueryArgs("database");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // keywordScore=0, tagMatches=0, titleMatches=1 → 1/1*0.2=0.2
        // type=User → 0.2, agedScore=0 → 0.1
        scored.RelevanceScore.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void ScoreMemory_TypeWeight_AppliesToRawScore() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var userMem = Make("database optimization", MemoryType.User);
        var refMem = Make("database optimization", MemoryType.Reference);
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var userScored = sut.ScoreMemory(userMem, words, ac, now);
        var refScored = sut.ScoreMemory(refMem, words, ac, now);

        // Reference weight=0.6, User weight=1.0
        // rawScore=0.4, User→0.4, Reference→0.24
        // agedScore=0 → User 0.2, Reference 0.12
        refScored.RelevanceScore.Should().BeApproximately(userScored.RelevanceScore * 0.6, 1e-9);
    }

    [Fact]
    public void ScoreMemory_AgedScore_BlendedFiftyFifty() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.4);
        var memory = Make("database optimization");
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // rawScore=0.4, type=User → 0.4
        // agedScore=0.4 → 0.4*0.5 + 0.4*0.5 = 0.4
        // accessCount=0 → 0.4
        scored.RelevanceScore.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void ScoreMemory_AccessCount_BoostsWithLogScale() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var lowAccess = Make("database optimization", accessCount: 0);
        var highAccess = Make("database optimization", accessCount: 9);
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var lowScored = sut.ScoreMemory(lowAccess, words, ac, now);
        var highScored = sut.ScoreMemory(highAccess, words, ac, now);

        // accessCount=0 → ×(1+log(1)*0.1)=1.0
        // accessCount=9 → ×(1+log(10)*0.1)≈1+0.2302=1.2302
        highScored.RelevanceScore.Should().BeApproximately(lowScored.RelevanceScore * (1 + Math.Log(10) * 0.1), 1e-6);
        highScored.RelevanceScore.Should().BeGreaterThan(lowScored.RelevanceScore);
    }

    [Fact]
    public void ScoreMemory_ScoreCappedAtOne() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(1.0);
        var memory = Make("database optimization", MemoryType.User, title: "database optimization", tags: new[] { "database", "optimization" }, accessCount: 1000);
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        scored.RelevanceScore.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void ScoreMemory_NoMatch_ReturnsZeroScore() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("unrelated content");
        var (words, ac) = BuildQueryArgs("xyz123");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        // 无任何匹配,rawScore=0, agedScore=0 → 0
        scored.RelevanceScore.Should().Be(0.0);
    }

    [Fact]
    public void ScoreMemory_PreservesMemoryReference() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.0);
        var memory = Make("database optimization");
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var scored = sut.ScoreMemory(memory, words, ac, now);

        scored.Memory.Should().BeSameAs(memory);
    }

    [Fact]
    public void ScoreMemory_Deterministic_SameInputSameOutput() {
        using var sut = CreateSut();
        _ageCalculatorMock.Setup(a => a.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime>())).Returns(0.3);
        var memory = Make("database optimization", accessCount: 5);
        var (words, ac) = BuildQueryArgs("database optimization");
        var now = _clock.GetUtcNow();

        var s1 = sut.ScoreMemory(memory, words, ac, now);
        var s2 = sut.ScoreMemory(memory, words, ac, now);
        s1.RelevanceScore.Should().Be(s2.RelevanceScore);
    }
}