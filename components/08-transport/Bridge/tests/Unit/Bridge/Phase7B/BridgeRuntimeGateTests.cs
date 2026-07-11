
namespace Bridge.Tests.Phase7B;

public sealed class BridgeRuntimeGateTests
{
    [Fact]
    public void IsBridgeEnabled_Default_ReturnsFalse()
    {
        BridgeRuntimeGate.Reset();
        // 未初始化时 IsBridgeEnabled 返回 false
        var result = BridgeRuntimeGate.IsBridgeEnabled();
        Assert.False(result);
    }

    [Fact]
    public void IsCseShimEnabled_Default_ReturnsTrue()
    {
        BridgeRuntimeGate.Reset();
        // 未初始化时 IsCseShimEnabled 默认返回 true（对齐 TS 端）
        var result = BridgeRuntimeGate.IsCseShimEnabled();
        Assert.True(result);
    }

    [Fact]
    public void IsEnvLessBridgeEnabled_Default_ReturnsFalse()
    {
        BridgeRuntimeGate.Reset();
        var result = BridgeRuntimeGate.IsEnvLessBridgeEnabled();
        Assert.False(result);
    }

    [Fact]
    public void GetCcrAutoConnectDefault_Default_ReturnsTrue()
    {
        BridgeRuntimeGate.Reset();
        // 默认 true（对齐 TS 端）
        var result = BridgeRuntimeGate.GetCcrAutoConnectDefault();
        Assert.True(result);
    }

    [Fact]
    public void IsCcrMirrorEnabled_Default_ReturnsFalse()
    {
        BridgeRuntimeGate.Reset();
        var result = BridgeRuntimeGate.IsCcrMirrorEnabled();
        Assert.False(result);
    }

    [Fact]
    public void SetCseShimEnabled_True_EnablesCseShim()
    {
        BridgeRuntimeGate.Reset();
        BridgeRuntimeGate.SetCseShimEnabled(true);
        Assert.True(BridgeRuntimeGate.IsCseShimEnabled());
        BridgeRuntimeGate.Reset();
    }

    [Fact]
    public async Task IsBridgeEnabledBlockingAsync_Default_ReturnsFalse()
    {
        BridgeRuntimeGate.Reset();
        var result = await BridgeRuntimeGate.IsBridgeEnabledBlockingAsync().ConfigureAwait(true);
        Assert.False(result);
    }
}
