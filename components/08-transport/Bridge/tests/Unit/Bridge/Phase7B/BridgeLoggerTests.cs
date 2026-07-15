
namespace Bridge.Tests.Phase7B;

public sealed class NullBridgeLoggerTests
{
    [Fact]
    public void NullBridgeLogger_DoesNotThrow()
    {
        IBridgeLogger logger = new NullBridgeLogger();

        // 所有方法调用不应抛异常
        logger.PrintBanner(new BridgeConfig(), "env1");
        logger.UpdateIdleStatus();
        logger.UpdateReconnectingStatus("5s", "10s");
        logger.UpdateSessionStatus("session1", "1m", BridgeSessionActivity.Thinking, []);
        logger.ClearStatus();
        logger.SetRepoInfo("repo", "main");
        logger.SetDebugLogPath("/tmp/debug.log");
        logger.SetAttached("session1");
        logger.UpdateFailedStatus("connection lost");
        logger.ToggleQr();
        logger.UpdateSessionCount(1, 5, BridgeSpawnMode.SingleSession);
        logger.SetSpawnModeDisplay(BridgeSpawnMode.Worktree);
        logger.AddSession("session1", "http://example.com");
        logger.UpdateSessionActivity("session1", BridgeSessionActivity.ToolUse);
        logger.SetSessionTitle("session1", "My Session");
        logger.RemoveSession("session1");
        logger.RefreshDisplay();

        Assert.True(true); // 无异常即通过
    }

    [Fact]
    public void BridgeStatusState_EnumValues()
    {
        Assert.True(Enum.IsDefined<BridgeStatusState>(BridgeStatusState.Idle));
        Assert.True(Enum.IsDefined<BridgeStatusState>(BridgeStatusState.Attached));
        Assert.True(Enum.IsDefined<BridgeStatusState>(BridgeStatusState.Reconnecting));
        Assert.True(Enum.IsDefined<BridgeStatusState>(BridgeStatusState.Failed));
        Assert.True(Enum.IsDefined<BridgeStatusState>(BridgeStatusState.Titled));
    }

    [Fact]
    public void BridgeSessionActivity_EnumValues()
    {
        Assert.True(Enum.IsDefined<BridgeSessionActivity>(BridgeSessionActivity.Idle));
        Assert.True(Enum.IsDefined<BridgeSessionActivity>(BridgeSessionActivity.Thinking));
        Assert.True(Enum.IsDefined<BridgeSessionActivity>(BridgeSessionActivity.Responding));
        Assert.True(Enum.IsDefined<BridgeSessionActivity>(BridgeSessionActivity.ToolUse));
        Assert.True(Enum.IsDefined<BridgeSessionActivity>(BridgeSessionActivity.WaitingForInput));
    }
}
