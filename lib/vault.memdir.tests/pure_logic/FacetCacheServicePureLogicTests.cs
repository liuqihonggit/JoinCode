
namespace Core.Tests.Memdir;

/// <summary>
/// FacetCacheService 纯逻辑确定性测试
/// IsValidFacets: 必要字段校验
/// GetFacetFilePath: sessionId 路径分隔符清理
/// </summary>
public sealed class FacetCacheServicePureLogicTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();

    private FacetCacheService CreateSut(string? dir = "/test/facets")
        => new(_fs, dir, NullLogger<FacetCacheService>.Instance);

    private static SessionFacets MakeFacets(
        string? underlyingGoal = "optimize performance",
        string outcome = "fully_achieved",
        string briefSummary = "user wanted speed, got it",
        IReadOnlyDictionary<string, int>? goalCategories = null,
        IReadOnlyDictionary<string, int>? userSatisfactionCounts = null,
        IReadOnlyDictionary<string, int>? frictionCounts = null) => new() {
        SessionId = "s1",
        UnderlyingGoal = underlyingGoal ?? string.Empty,
        Outcome = outcome,
        BriefSummary = briefSummary,
        GoalCategories = goalCategories ?? new Dictionary<string, int> { ["performance"] = 1 },
        UserSatisfactionCounts = userSatisfactionCounts ?? new Dictionary<string, int> { ["satisfied"] = 1 },
        FrictionCounts = frictionCounts ?? new Dictionary<string, int>()
    };

    // === IsValidFacets: 合法 facet ===

    [Fact]
    public void IsValidFacets_AllRequiredFieldsPresent_ReturnsTrue() {
        FacetCacheService.IsValidFacets(MakeFacets()).Should().BeTrue();
    }

    [Fact]
    public void IsValidFacets_WithEmptyFrictionCounts_ReturnsTrue() {
        FacetCacheService.IsValidFacets(MakeFacets(frictionCounts: new Dictionary<string, int>())).Should().BeTrue();
    }

    [Fact]
    public void IsValidFacets_WithNonEmptyFrictionCounts_ReturnsTrue() {
        FacetCacheService.IsValidFacets(MakeFacets(frictionCounts: new Dictionary<string, int> { ["slow"] = 2 })).Should().BeTrue();
    }

    // === IsValidFacets: 缺失必要字段 ===

    [Fact]
    public void IsValidFacets_EmptyUnderlyingGoal_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(underlyingGoal: "")).Should().BeFalse();
    }

    [Fact]
    public void IsValidFacets_NullUnderlyingGoal_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(underlyingGoal: null)).Should().BeFalse();
    }

    [Fact]
    public void IsValidFacets_EmptyOutcome_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(outcome: "")).Should().BeFalse();
    }

    [Fact]
    public void IsValidFacets_EmptyBriefSummary_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(briefSummary: "")).Should().BeFalse();
    }

    [Fact]
    public void IsValidFacets_EmptyGoalCategories_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(goalCategories: new Dictionary<string, int>())).Should().BeFalse();
    }

    [Fact]
    public void IsValidFacets_EmptyUserSatisfactionCounts_ReturnsFalse() {
        FacetCacheService.IsValidFacets(MakeFacets(userSatisfactionCounts: new Dictionary<string, int>())).Should().BeFalse();
    }

    // === IsValidFacets: 确定性 ===

    [Fact]
    public void IsValidFacets_Deterministic_SameInputSameOutput() {
        var facets = MakeFacets();
        var r1 = FacetCacheService.IsValidFacets(facets);
        var r2 = FacetCacheService.IsValidFacets(facets);
        r1.Should().Be(r2);
    }

    // === GetFacetFilePath: 路径拼接 ===

    [Fact]
    public void GetFacetFilePath_PlainSessionId_CombinesDirSessionIdFileName() {
        var sut = CreateSut("/test/facets");
        var path = sut.GetFacetFilePath("session-001");
        path.Should().Be(Path.Combine("/test/facets", "session-001", "usage-facet.json"));
    }

    [Fact]
    public void GetFacetFilePath_SessionIdWithForwardSlash_ReplacedWithUnderscore() {
        var sut = CreateSut("/test/facets");
        var path = sut.GetFacetFilePath("dir/sub/session");
        path.Should().Be(Path.Combine("/test/facets", "dir_sub_session", "usage-facet.json"));
    }

    [Fact]
    public void GetFacetFilePath_SessionIdWithBackslash_ReplacedWithUnderscore() {
        var sut = CreateSut("/test/facets");
        var path = sut.GetFacetFilePath(@"dir\sub\session");
        path.Should().Be(Path.Combine("/test/facets", "dir_sub_session", "usage-facet.json"));
    }

    [Fact]
    public void GetFacetFilePath_SessionIdWithMixedSeparators_AllReplacedWithUnderscore() {
        var sut = CreateSut("/test/facets");
        var path = sut.GetFacetFilePath(@"dir/sub\mix");
        path.Should().Be(Path.Combine("/test/facets", "dir_sub_mix", "usage-facet.json"));
    }

    [Fact]
    public void GetFacetFilePath_AlwaysEndsWithUsageFacetJson() {
        var sut = CreateSut("/test/facets");
        var path = sut.GetFacetFilePath("any-session-id");
        path.Should().EndWith("usage-facet.json");
    }

    // === GetFacetFilePath: 确定性 ===

    [Fact]
    public void GetFacetFilePath_Deterministic_SameInputSameOutput() {
        var sut = CreateSut("/test/facets");
        var p1 = sut.GetFacetFilePath("session-1");
        var p2 = sut.GetFacetFilePath("session-1");
        p1.Should().Be(p2);
    }
}
