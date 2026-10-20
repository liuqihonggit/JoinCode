namespace NativeTui.Host.Tests;

/// <summary>
/// EngineSession 轮次结果翻译测试 — SessionTurnResult → EngineTurnClient 异常语义：
/// 成功=null（client 发 turn_completed）；取消=OperationCanceledException（turn_interrupted）；
/// 超时/错误=InvalidOperationException（client 发 Error 事件，超时消息含 Host 交互诱导 — Esc 打断 + 网络检查）。
/// 组装链路本身（引擎 DI + 权限真实回执）由 Program --selftest 真实冒烟覆盖（ADR 0080，非 mock）。
/// </summary>
public sealed class EngineSessionTests {
    [Fact]
    public void TranslateTurnResult_Success_ReturnsNull() {
        var ex = EngineSession.TranslateTurnResult(SessionTurnResult.Success("回答", DateTime.UtcNow));
        Assert.Null(ex);
    }

    [Fact]
    public void TranslateTurnResult_Cancelled_ReturnsOperationCanceled() {
        var ex = EngineSession.TranslateTurnResult(SessionTurnResult.FromCancellation("部分回答"));
        Assert.IsType<OperationCanceledException>(ex);
    }

    [Fact]
    public void TranslateTurnResult_Timeout_ReturnsInductiveError() {
        var ex = EngineSession.TranslateTurnResult(SessionTurnResult.Timeout(30_000));
        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Esc", invalid.Message);
        Assert.Contains("30000", invalid.Message);
    }

    [Fact]
    public void TranslateTurnResult_Error_CarriesEngineMessage() {
        var ex = EngineSession.TranslateTurnResult(SessionTurnResult.Error("模型限流", partialResponse: string.Empty));
        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("模型限流", invalid.Message);
    }

    [Fact]
    public void TranslateTurnResult_UnknownError_FallsBackToDefaultMessage() {
        var ex = EngineSession.TranslateTurnResult(new SessionTurnResult());
        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("未知错误", invalid.Message);
    }
}
