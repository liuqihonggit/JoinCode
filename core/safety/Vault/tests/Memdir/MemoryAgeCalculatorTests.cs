
namespace Core.Tests.Memdir;

public sealed class MemoryAgeCalculatorTests
{
    private readonly MemoryAgeCalculator _sut = new();
    private readonly DateTime _now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CalculateAgedRelevance_FreshEntry_ReturnsBaseScore()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "fresh", now: _now);

        var score = _sut.CalculateAgedRelevance(entry, _now);

        score.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void CalculateAgedRelevance_ExpiredEntry_ReturnsMinScore()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "expired", ttl: TimeSpan.FromSeconds(-1), now: _now);

        var score = _sut.CalculateAgedRelevance(entry, _now);

        score.Should().Be(0.1);
    }

    [Fact]
    public void CalculateAgedRelevance_AccessCount_BoostsScore()
    {
        // 创建一个有轻微衰减的条目，使基础分数 < 1.0，这样 accessBonus 才能提升分数
        var olderTime = _now.AddDays(-10);
        var entry = MemoryEntry.Create(MemoryType.User, "accessed", now: olderTime);
        var accessed = entry with { AccessCount = 5 };

        var baseScore = _sut.CalculateAgedRelevance(entry, _now);
        var score = _sut.CalculateAgedRelevance(accessed, _now);

        score.Should().BeGreaterThan(baseScore);
    }

    [Fact]
    public void CalculateAgedRelevance_AccessCount_IsCapped()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "accessed", now: _now);
        var accessed = entry with { AccessCount = 1000 };

        var score = _sut.CalculateAgedRelevance(accessed, _now);

        score.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void ShouldArchive_AlreadyArchived_ReturnsFalse()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "archived", now: _now).WithArchived(_now);

        var result = _sut.ShouldArchive(entry, _now);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldArchive_Expired_ReturnsTrue()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "expired", ttl: TimeSpan.FromSeconds(-1), now: _now);

        var result = _sut.ShouldArchive(entry, _now);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldArchive_BelowArchiveThreshold_ReturnsTrue()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "old", now: _now);
        var old = entry with { CreatedAt = _now.AddYears(2) };

        var result = _sut.ShouldArchive(old, _now.AddYears(2).AddDays(1));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldArchive_YoungAndRelevant_ReturnsFalse()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "fresh", now: _now);

        var result = _sut.ShouldArchive(entry, _now);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldArchive_ExceedsMaxAge_ReturnsTrue()
    {
        var entry = MemoryEntry.Create(MemoryType.User, "ancient", now: _now);
        var future = _now.AddDays(400);

        var result = _sut.ShouldArchive(entry, future);

        result.Should().BeTrue();
    }
}
