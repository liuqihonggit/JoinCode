
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryRelevanceScorer 相关性评分确定性测试
/// 验证 6 加权因子: 关键词匹配/整词加成/标签匹配/类型匹配/访问频率/时间衰减
/// 使用 FakeClockService 固定时间,消除时序依赖
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class MemoryRelevanceScorerTests {
    private readonly FakeClockService _clock = new(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

    private MemoryRelevanceScorer CreateSut() => new(_clock);

    private static MemoryEntry Make(
        string content,
        MemoryType type = MemoryType.User,
        string? title = null,
        IEnumerable<string>? tags = null,
        int accessCount = 0,
        DateTime? createdAt = null) {
        var now = createdAt ?? new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var entry = MemoryEntry.Create(type, content, title: title, tags: tags, now: now);
        return entry with { AccessCount = accessCount };
    }

    // === 关键词匹配因子 ===

    [Fact]
    public void CalculateAdvancedRelevanceScore_ContentMatch_ContributesScore() {
        var sut = CreateSut();
        var memory = Make("database optimization solution");
        // queryWords = [database, optimization]
        // "database" 子串匹配 +1.0, 整词 +0.5 = 1.5
        // "optimization" 子串匹配 +1.0, 整词 +0.5 = 1.5
        // 总 3.0, AccessCount=0 → log(1)=0 → ×1.0, daysSinceCreated=0 → exp(0)=1.0
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database optimization");
        score.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_PartialMatch_OnlySubstringNoWholeWordBonus() {
        var sut = CreateSut();
        // "data" 是 "database" 的子串,但不是整词
        var memory = Make("database content");
        // "data" 子串匹配 +1.0, 整词不匹配 +0
        var score = sut.CalculateAdvancedRelevanceScore(memory, "data");
        score.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_NoMatch_ReturnsZero() {
        var sut = CreateSut();
        var memory = Make("unrelated content");
        var score = sut.CalculateAdvancedRelevanceScore(memory, "xyz123");
        score.Should().Be(0.0);
    }

    // === 标签匹配因子(权重 2.0)===

    [Fact]
    public void CalculateAdvancedRelevanceScore_TagMatch_ContributesTwoPointsPerTag() {
        var sut = CreateSut();
        var memory = Make("some content", tags: new[] { "database" });
        // "database" 标签匹配 → +2.0
        // "database" 不是 content 的子串 → 无关键词匹配
        // 总 2.0
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database");
        score.Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_MultipleTagMatches_ContributesTwoPointsEach() {
        var sut = CreateSut();
        var memory = Make("some content", tags: new[] { "database", "optimization" });
        // 两个标签都匹配 → +4.0
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database optimization");
        score.Should().BeApproximately(4.0, 1e-9);
    }

    // === 类型匹配因子(权重 1.5)===

    [Fact]
    public void CalculateAdvancedRelevanceScore_TypeMatch_ContributesOnePointFive() {
        var sut = CreateSut();
        // type=User, query 包含 "user" → 类型匹配 +1.5
        var memory = Make("some content", MemoryType.User);
        var score = sut.CalculateAdvancedRelevanceScore(memory, "user");
        score.Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_TypeMatch_FeedbackMatchesFeedbackQuery() {
        var sut = CreateSut();
        var memory = Make("some content", MemoryType.Feedback);
        // "feedback" 类型匹配 +1.5
        var score = sut.CalculateAdvancedRelevanceScore(memory, "feedback");
        score.Should().BeApproximately(1.5, 1e-9);
    }

    // === 访问频率因子 ===

    [Fact]
    public void CalculateAdvancedRelevanceScore_AccessCount_BoostsScore() {
        var sut = CreateSut();
        var lowAccess = Make("database content", accessCount: 0);
        var highAccess = Make("database content", accessCount: 9);

        var lowScore = sut.CalculateAdvancedRelevanceScore(lowAccess, "database");
        var highScore = sut.CalculateAdvancedRelevanceScore(highAccess, "database");
        // AccessCount=0 → ×(1+log(1))=1.0; AccessCount=9 → ×(1+log(10))≈1+2.302=3.302
        highScore.Should().BeGreaterThan(lowScore);
        highScore.Should().BeApproximately(lowScore * (1 + Math.Log(10)), 1e-6);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_ZeroAccessCount_DoesNotBoostScore() {
        var sut = CreateSut();
        var memory = Make("database content", accessCount: 0);
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database");
        // ×(1+log(1)) = ×1.0
        score.Should().BeApproximately(1.5, 1e-9);
    }

    // === 时间衰减因子 ===

    [Fact]
    public void CalculateAdvancedRelevanceScore_RecentMemory_HigherScoreThanOldMemory() {
        var sut = CreateSut();
        var now = _clock.GetUtcNow();
        var recent = Make("database content", createdAt: now);
        var old = Make("database content", createdAt: now.AddDays(-60));

        var recentScore = sut.CalculateAdvancedRelevanceScore(recent, "database");
        var oldScore = sut.CalculateAdvancedRelevanceScore(old, "database");
        // 越新的分数越高
        recentScore.Should().BeGreaterThan(oldScore);
        // 60 天衰减: exp(-60/30) = exp(-2) ≈ 0.1353
        oldScore.Should().BeApproximately(recentScore * Math.Exp(-2.0), 1e-6);
    }

    [Fact]
    public void CalculateAdvancedRelevanceScore_ZeroDaysSinceCreated_NoDecay() {
        var sut = CreateSut();
        var now = _clock.GetUtcNow();
        var memory = Make("database content", createdAt: now);
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database");
        // exp(0) = 1.0, 无衰减
        score.Should().BeApproximately(1.5, 1e-9);
    }

    // === 组合因子 ===

    [Fact]
    public void CalculateAdvancedRelevanceScore_CombinesAllFactors() {
        var sut = CreateSut();
        var now = _clock.GetUtcNow();
        var memory = Make("database optimization", MemoryType.User, tags: new[] { "database" }, accessCount: 9, createdAt: now);

        // 关键词: "database" 整词 1.5 + "optimization" 整词 1.5 = 3.0
        // 标签: "database" 匹配 +2.0 = 5.0
        // 类型: "user" 不在 query → 无
        // AccessCount=9: ×(1+log(10)) ≈ 3.302
        // 时间: exp(0)=1.0
        var score = sut.CalculateAdvancedRelevanceScore(memory, "database optimization");
        var expected = 5.0 * (1 + Math.Log(10));
        score.Should().BeApproximately(expected, 1e-6);
    }

    // === GetMatchReason ===

    [Fact]
    public void GetMatchReason_ContentMatch_ReturnsContentReason() {
        var sut = CreateSut();
        var memory = Make("database content");
        var reason = sut.GetMatchReason(memory, "database");
        reason.Should().NotBeNull();
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonContent));
    }

    [Fact]
    public void GetMatchReason_TagMatch_ReturnsTagReason() {
        var sut = CreateSut();
        var memory = Make("unrelated", tags: new[] { "database" });
        var reason = sut.GetMatchReason(memory, "database");
        reason.Should().NotBeNull();
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonTag));
    }

    [Fact]
    public void GetMatchReason_TypeMatch_ReturnsTypeReason() {
        var sut = CreateSut();
        var memory = Make("unrelated", MemoryType.User);
        var reason = sut.GetMatchReason(memory, "user");
        reason.Should().NotBeNull();
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonType));
    }

    [Fact]
    public void GetMatchReason_AllThreeMatch_ReturnsAllReasonsJoined() {
        var sut = CreateSut();
        var memory = Make("user content", MemoryType.User, tags: new[] { "user" });
        var reason = sut.GetMatchReason(memory, "user");
        reason.Should().NotBeNull();
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonContent));
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonTag));
        reason.Should().Contain(L.T(StringKey.VaultMatchReasonType));
    }

    [Fact]
    public void GetMatchReason_NoMatch_ReturnsNull() {
        var sut = CreateSut();
        var memory = Make("unrelated content");
        var reason = sut.GetMatchReason(memory, "xyz123");
        reason.Should().BeNull();
    }

    // === 确定性 ===

    [Fact]
    public void CalculateAdvancedRelevanceScore_Deterministic_SameInputSameOutput() {
        var sut = CreateSut();
        var memory = Make("database content", accessCount: 5);
        var s1 = sut.CalculateAdvancedRelevanceScore(memory, "database");
        var s2 = sut.CalculateAdvancedRelevanceScore(memory, "database");
        s1.Should().Be(s2);
    }

    // === 守卫补全确定性测试 (TASK031) ===

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Ctor_NullClock_ThrowsArgumentNullException() {
        var act = () => new MemoryRelevanceScorer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void CalculateAdvancedRelevanceScore_NullMemory_ThrowsArgumentNullException() {
        var sut = CreateSut();
        var act = () => sut.CalculateAdvancedRelevanceScore(null!, "query");
        act.Should().Throw<ArgumentNullException>().WithParameterName("memory");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void CalculateAdvancedRelevanceScore_NullQuery_ThrowsArgumentNullException() {
        var sut = CreateSut();
        var memory = Make("content");
        var act = () => sut.CalculateAdvancedRelevanceScore(memory, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("query");
    }
}
