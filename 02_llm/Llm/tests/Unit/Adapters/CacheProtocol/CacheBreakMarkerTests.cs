namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class CacheBreakMarkerTests
{
    [Fact]
    public void Create_ShouldReturnMetadataWithCacheBreakKey()
    {
        var metadata = CacheBreakMarker.Create();

        metadata.Should().NotBeNull();
        metadata.ContainsKey(CacheBreakMarker.MetadataKey).Should().BeTrue(
            "CacheBreakMarker.Create() must produce metadata with the 'CacheBreak' key");
    }

    [Fact]
    public void Create_CacheBreakValueShouldBeTrue()
    {
        var metadata = CacheBreakMarker.Create();

        metadata[CacheBreakMarker.MetadataKey].ValueKind.Should().Be(JsonValueKind.True,
            "CacheBreak marker value must be boolean true");
    }

    [Fact]
    public void MetadataKey_ShouldBeCacheBreak()
    {
        CacheBreakMarker.MetadataKey.Should().Be("CacheBreak");
    }
}
