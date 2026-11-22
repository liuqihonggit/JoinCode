// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018

// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests {
    #region HeadlessBridgeLogger — 显示适配器

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void HeadlessBridgeLogger_NullLog_Throws() {
        Assert.Throws<ArgumentNullException>(() => new HeadlessBridgeLogger(null!));
    }

    [Fact]
    public void HeadlessBridgeLogger_PrintBanner_RoutesToLog() {
        var messages = new List<string>();
        var logger = new HeadlessBridgeLogger(messages.Add);

        var config = new BridgeConfig {
            Dir = "C:\\test",
            SpawnMode = BridgeSpawnMode.SameDir,
            MaxSessions = 5,
        };
        logger.PrintBanner(config, "env-123");

        Assert.Single(messages);
        Assert.Contains("env-123", messages[0]);
        Assert.Contains("C:\\test", messages[0]);
        Assert.Contains("same-dir", messages[0]);
        Assert.Contains("5", messages[0]);
    }

    [Fact]
    public void HeadlessBridgeLogger_AddSession_IgnoresUrl() {
        var messages = new List<string>();
        var logger = new HeadlessBridgeLogger(messages.Add);

        logger.AddSession("sess-1", "wss://example.com/ws");

        Assert.Single(messages);
        Assert.Contains("sess-1", messages[0]);
        Assert.DoesNotContain("wss://", messages[0]);
    }

    [Fact]
    public void HeadlessBridgeLogger_RemoveSession_RoutesToLog() {
        var messages = new List<string>();
        var logger = new HeadlessBridgeLogger(messages.Add);

        logger.RemoveSession("sess-1");

        Assert.Single(messages);
        Assert.Contains("sess-1", messages[0]);
        Assert.Contains("detached", messages[0]);
    }

    [Fact]
    public void HeadlessBridgeLogger_TuiMethods_AreNoop() {
        var messages = new List<string>();
        var logger = new HeadlessBridgeLogger(messages.Add);

        logger.UpdateIdleStatus();
        logger.UpdateReconnectingStatus("5s", "10s");
        logger.UpdateSessionStatus("s1", "1m", BridgeSessionActivity.Thinking, Array.Empty<string>());
        logger.ClearStatus();
        logger.SetRepoInfo("repo", "main");
        logger.SetDebugLogPath("C:\\debug.log");
        logger.SetAttached("s1");
        logger.UpdateFailedStatus("error");
        logger.ToggleQr();
        logger.UpdateSessionCount(1, 5, BridgeSpawnMode.SameDir);
        logger.SetSpawnModeDisplay(BridgeSpawnMode.Worktree);
        logger.UpdateSessionActivity("s1", BridgeSessionActivity.Responding);
        logger.SetSessionTitle("s1", "My Session");
        logger.RefreshDisplay();

        Assert.Empty(messages);
    }

    #endregion
}