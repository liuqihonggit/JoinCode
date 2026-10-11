// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// StdioProcessManager 守卫确定性测试 — config/message null + timeout 负数范围
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class StdioProcessManagerTest {
    [Fact]
    public async Task StartAsync_NullConfig_ThrowsArgumentNullException() {
        await using var mgr = new StdioProcessManager();
        var act = async () => await mgr.StartAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public async Task StartAsync_EmptyExecutablePath_ThrowsArgumentException() {
        await using var mgr = new StdioProcessManager();
        var config = new StdioProcessConfig { ExecutablePath = "" };
        var act = async () => await mgr.StartAsync(config);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("config.ExecutablePath");
    }

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException() {
        await using var mgr = new StdioProcessManager();
        var act = async () => await mgr.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public async Task WaitForOutputAsync_NegativeTimeout_ThrowsArgumentOutOfRangeException() {
        await using var mgr = new StdioProcessManager();
        var act = async () => await mgr.WaitForOutputAsync(_ => true, TimeSpan.FromSeconds(-1));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Fact]
    public async Task WaitForErrorAsync_NegativeTimeout_ThrowsArgumentOutOfRangeException() {
        await using var mgr = new StdioProcessManager();
        var act = async () => await mgr.WaitForErrorAsync(_ => true, TimeSpan.FromSeconds(-1));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }
}
