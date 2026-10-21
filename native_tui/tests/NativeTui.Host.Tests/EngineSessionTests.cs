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

    /// <summary>session_info 事件构造（修复验收缺陷：/model 切换后 banner 不同步）：
    /// SetPrimaryModel 后 BuildSessionInfoEvent 反映新模型 — ApplyModel 委托刷新 banner 的数据基础。
    /// FIFO 保证：SetPrimaryModelCmd 先入队，GetPrimaryModelQuery 后入队，QuerySync 返回时状态已更新（确定性无时序）。
    /// ⚠️ Dispose 前完成全部查询 — TrySend 在释放后静默返回 false，QuerySync 将永久阻塞。</summary>
    [Fact]
    public async Task BuildSessionInfoEvent_AfterModelSwitch_ReflectsNewModel() {
        await using var fastMode = new FastModeService(
            config: new WorkflowConfig { Provider = new ProviderConfig { ModelId = "initial-model" } });
        var before = EngineSession.BuildSessionInfoEvent(checker: null, fastMode);
        Assert.Equal("initial-model", before.Model);

        fastMode.SetPrimaryModel("switched-model");
        var after = EngineSession.BuildSessionInfoEvent(checker: null, fastMode);
        Assert.Equal("switched-model", after.Model);
    }

    /// <summary>session_info 事件供应商字段（TASK058）— WorkflowConfig.Provider.Vendor 透传；无 config 时 Vendor 为 null（旧引擎兼容退化）。</summary>
    [Fact]
    public async Task BuildSessionInfoEvent_WithConfig_CarriesVendor()
    {
        await using var fastMode = new FastModeService();
        var config = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "zhipu" } };

        var withConfig = EngineSession.BuildSessionInfoEvent(checker: null, fastMode, config);
        Assert.Equal("zhipu", withConfig.Vendor);

        var withoutConfig = EngineSession.BuildSessionInfoEvent(checker: null, fastMode, config: null);
        Assert.Null(withoutConfig.Vendor);
    }

    /// <summary>登录态判定（TASK060 TUI 提交门禁）— ApiKey 非空 = 已登录；空 = 未登录（TUI 本地反馈不调引擎）。</summary>
    [Fact]
    public async Task BuildSessionInfoEvent_LoginStateFollowsApiKey()
    {
        await using var fastMode = new FastModeService();
        var withKey = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "sensenova", ApiKey = "sk-有效密钥" } };
        var withoutKey = new WorkflowConfig { Provider = new ProviderConfig { Vendor = "sensenova", ApiKey = "" } };

        Assert.True(EngineSession.BuildSessionInfoEvent(checker: null, fastMode, withKey).LoggedIn);
        Assert.False(EngineSession.BuildSessionInfoEvent(checker: null, fastMode, withoutKey).LoggedIn);
    }

    /// <summary>TUI 静默日志（修复：引擎 Console 日志直写 stderr 与 inline 渲染交错致光标错乱/横向线残影）—
    /// provider 必须是 SilentLoggerProvider（禁止 Console 系 provider）。</summary>
    [Fact]
    public void LoggerProvider_IsSilent_NotConsoleProvider()
    {
        Assert.Same(SilentLoggerProvider.Instance, EngineSession.LoggerProvider);
        Assert.DoesNotContain("Console", EngineSession.LoggerProvider.GetType().Name, StringComparison.Ordinal);
    }
}
