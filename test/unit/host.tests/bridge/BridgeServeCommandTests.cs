namespace Host.Tests.Bridge;

/// <summary>
/// bridge_serve 命令层测试 — SessionTurnResult → BridgeServeLoop 异常语义翻译契约：
/// 成功=不抛（loop 发 turn_completed）；取消=OperationCanceledException（turn_interrupted）；
/// 超时/错误=InvalidOperationException（error 事件携带诱导消息）。
/// </summary>
public sealed class BridgeServeCommandTests {
    [Fact]
    public void TranslateTurnResult_Success_ReturnsNull() {
        var ex = BridgeServeCommand.TranslateTurnResult(SessionTurnResult.Success("ok", DateTime.UtcNow));
        ex.Should().BeNull();
    }

    [Fact]
    public void TranslateTurnResult_Cancelled_ThrowsOperationCanceled() {
        var ex = BridgeServeCommand.TranslateTurnResult(SessionTurnResult.FromCancellation("部分"));
        ex.Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public void TranslateTurnResult_TimedOut_ThrowsWithInductiveMessage() {
        var ex = BridgeServeCommand.TranslateTurnResult(SessionTurnResult.Timeout(10_000));
        ex.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("超时").And.Contain("10000");
    }

    [Fact]
    public void TranslateTurnResult_Error_ThrowsWithErrorMessage() {
        var ex = BridgeServeCommand.TranslateTurnResult(SessionTurnResult.Error("LLM 连接失败", ""));
        ex.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("LLM 连接失败");
    }

    [Fact]
    public void BuildSessionInfo_FastModeModelWins_ThenOptionsFallback_ThenNull() {
        var fastMode = new FakeFastModeService("glm-4.7");

        var withFastMode = BridgeServeCommand.BuildSessionInfo(null, fastMode, new CommandLineOptions { Model = "fallback-model" });
        withFastMode.Model.Should().Be("glm-4.7");

        var withOptionsFallback = BridgeServeCommand.BuildSessionInfo(null, new FakeFastModeService(""), new CommandLineOptions { Model = "cli-model" });
        withOptionsFallback.Model.Should().Be("cli-model");

        var withNothing = BridgeServeCommand.BuildSessionInfo(null, new FakeFastModeService(" "), new CommandLineOptions());
        withNothing.Model.Should().BeNull();
    }

    [Fact]
    public void BuildSessionInfo_WithoutChecker_DefaultsToAutoAndBypassAvailable() {
        var info = BridgeServeCommand.BuildSessionInfo(null, null, new CommandLineOptions());

        info.PermissionMode.Should().Be("auto");
        info.BypassAvailable.Should().BeTrue();
        info.Version.Should().NotBeNullOrEmpty();
        info.Cwd.Should().NotBeNullOrEmpty();
    }

    /// <summary>FastModeService 假实现（仅 PrimaryModelId 参与 session_info 组装）。</summary>
    private sealed class FakeFastModeService(string primaryModelId) : IFastModeService {
        public bool IsFastModeActive => false;
        public string FastModelId => string.Empty;
        public string PrimaryModelId => primaryModelId;
        public event EventHandler<FastModeChangedEventArgs>? FastModeChanged { add { } remove { } }
        public void Activate() { }
        public void Deactivate() { }
        public void Toggle() { }
        public void SetFastModel(string modelId) { }
        public void SetPrimaryModel(string modelId) { }
    }
}
