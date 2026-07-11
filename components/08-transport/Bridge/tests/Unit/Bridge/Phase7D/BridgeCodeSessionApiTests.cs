
namespace Bridge.Tests.Phase7D;

public sealed class BridgeCodeSessionApiTests
{
    [Fact]
    public void BridgeRemoteCredentials_EpochFromString_ParsesCorrectly()
    {
        // protojson int64 序列化为字符串
        var epoch = BridgeRemoteCredentials.ParseWorkerEpoch("12345678901234");
        Assert.Equal(12345678901234L, epoch);
    }

    [Fact]
    public void BridgeRemoteCredentials_EpochFromNumber_ParsesCorrectly()
    {
        var epoch = BridgeRemoteCredentials.ParseWorkerEpoch(42.0);
        Assert.Equal(42L, epoch);
    }

    [Fact]
    public void BridgeRemoteCredentials_EpochInvalid_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => BridgeRemoteCredentials.ParseWorkerEpoch("not-a-number"));
    }

    [Fact]
    public void BridgeRemoteCredentials_EpochFromJsonElement_Number()
    {
        var json = """{"epoch":42}""";
        var doc = JsonDocument.Parse(json);
        var epoch = BridgeRemoteCredentials.ParseWorkerEpoch(doc.RootElement.GetProperty("epoch"));
        Assert.Equal(42L, epoch);
    }

    [Fact]
    public void BridgeRemoteCredentials_EpochFromJsonElement_String()
    {
        var json = """{"epoch":"12345678901234"}""";
        var doc = JsonDocument.Parse(json);
        var epoch = BridgeRemoteCredentials.ParseWorkerEpoch(doc.RootElement.GetProperty("epoch"));
        Assert.Equal(12345678901234L, epoch);
    }
}
