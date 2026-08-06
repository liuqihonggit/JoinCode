namespace PrefixCache.Unit;

public sealed class SessionMetaUpdatedAtTests
{
    [Fact]
    public void ToMeta_WithTicks_PersistsUpdatedAt()
    {
        var stats = new SessionStats();
        stats.RecordTurn(new TokenUsage(100, 200));
        const long ticks = 638400000000000000L;

        var meta = stats.ToMeta(updatedAtUtcTicks: ticks);

        meta.UpdatedAtUtcTicks.Should().Be(ticks);
    }

    [Fact]
    public void ToMeta_WithoutTicks_DefaultsToZero()
    {
        var stats = new SessionStats();

        var meta = stats.ToMeta();

        meta.UpdatedAtUtcTicks.Should().Be(0);
    }

    [Fact]
    public void RoundTrip_SerializesUpdatedAtTicks()
    {
        const long ticks = 638400000000000000L;
        var meta = new SessionMeta { UpdatedAtUtcTicks = ticks };

        var json = SessionMetaSerializer.Serialize(meta);
        var back = SessionMetaSerializer.Deserialize(json);

        back.UpdatedAtUtcTicks.Should().Be(ticks);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutUpdatedAt_DefaultsZero()
    {
        const string legacy = """{"CacheHitTokens":1,"CacheMissTokens":2,"LastPromptTokens":3,"TurnCount":1,"TotalCostUsd":0.1}""";

        var meta = SessionMetaSerializer.Deserialize(legacy);

        meta.UpdatedAtUtcTicks.Should().Be(0);
    }
}
