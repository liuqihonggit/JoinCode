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
    public void TranslateTurnResult_PendingPermission_ThrowsOperationCanceledWithHint() {
        // PermissionPending 确认在 bridge 协议（无 UI）中按打断处理，消息带诱导说明
        var ex = BridgeServeCommand.TranslatePendingPermission();
        ex.Should().BeOfType<OperationCanceledException>()
            .Which.Message.Should().Contain("权限确认");
    }
}
