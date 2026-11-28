namespace Host.Tests.Adapters;

/// <summary>
/// TASK144 Phase 1 — 控制面接线后的**全链**验证：原始 NDJSON 行 → 解析 → 控制面 → 应答。
/// <para>与 <see cref="SdkControlPlaneTests"/> 的分工：那边测控制面本身（构造命令直接调 plane），
/// 这里测「接线没有漏行」—— 解析器不再分派 subtype，只把控制行降成
/// <see cref="JoinCode.Cli.Output.SdkControlCommand"/>，未实现 subtype 也由 plane 统一回 error。</para>
/// </summary>
public sealed class SdkControlIntegrationTests {
    /// <summary>
    /// interrupt：一条原始控制行必须得到一条 success 空载荷应答，并要求取消在途回合。
    /// </summary>
    [Fact]
    public void Interrupt_FromRawLine_AcknowledgesAndRequestsCancel() {
        var outcome = Send(
            """{"type":"control_request","request_id":"i-1","request":{"subtype":"interrupt"}}""");

        outcome.CancelActiveTurn.Should().BeTrue();
        var body = outcome.Response.Response;
        body.Subtype.Should().Be("success");
        body.RequestId.Should().Be("i-1");
        body.Response.Should().BeEmpty();
    }

    /// <summary>
    /// set_permission_mode 合法值：应答必须带**实际生效值**（bypass 被安全闸拦成 ask 时要回 ask）。
    /// </summary>
    [Fact]
    public void SetPermissionMode_FromRawLine_ReportsEffectiveMode() {
        var outcome = Send(
            """{"type":"control_request","request_id":"pm-ok","request":{"subtype":"set_permission_mode","mode":"bypass"}}""",
            mode => mode == "bypass" ? "ask" : mode);

        var body = outcome.Response.Response;
        body.Subtype.Should().Be("success");
        body.RequestId.Should().Be("pm-ok");
        body.Response.Should().ContainKey("mode").WhoseValue.Should().Be("ask");
    }

    /// <summary>非法 mode：解析照常产出命令（解析器不再判断值是否合法），失败由 plane 统一回 error。</summary>
    [Fact]
    public void SetPermissionMode_UnknownMode_FromRawLine_RepliesError() {
        var outcome = Send(
            """{"type":"control_request","request_id":"pm-bad","request":{"subtype":"set_permission_mode","mode":"bogus"}}""",
            mode => mode == "plan" ? mode : null);

        var body = outcome.Response.Response;
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("pm-bad");
        body.Error.Should().Contain("bogus").And.Contain(PermissionModeApplierFactory.ValidPermissionModes);
    }

    /// <summary>缺 mode：同样必须回 error（TASK143 真机打回过「只写 stderr」）。</summary>
    [Fact]
    public void SetPermissionMode_WithoutMode_FromRawLine_RepliesError() {
        var outcome = Send(
            """{"type":"control_request","request_id":"pm-none","request":{"subtype":"set_permission_mode"}}""",
            mode => mode);

        var body = outcome.Response.Response;
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("pm-none");
        body.Error.Should().Contain("mode");
    }

    /// <summary>
    /// 未实现 subtype：解析器只降级、不判实现，plane 回 error 并列出当前支持清单。
    /// 这条是 TASK143 与 TASK144 的语义交界 —— 拒绝文案从此只由注册表生成。
    /// </summary>
    [Fact]
    public void UnsupportedSubtype_FromRawLine_RepliesErrorWithSameRequestId() {
        var outcome = Send(
            """{"type":"control_request","request_id":"m-1","request":{"subtype":"set_model","model":"x"}}""");

        var body = outcome.Response.Response;
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("m-1");
        body.Error.Should().Contain("set_model").And.Contain("interrupt").And.Contain("set_permission_mode");
    }

    /// <summary>
    /// 缺 request_id 的控制行无从回包 —— 解析阶段就必须降级为 stderr 诊断，不能产出命令让 plane 抛异常，
    /// 否则宿主读循环会因为一条畸形输入整轮崩掉。
    /// </summary>
    [Fact]
    public void ControlLineWithoutRequestId_IsDiagnosedNotDispatched() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"control_request","request":{"subtype":"interrupt"}}""");

        parsed.Control.Should().BeNull("没有 request_id 就没有应答对象，不能进控制面");
        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().Contain("request_id");
    }

    /// <summary>普通 user 行不得被误判成控制行。</summary>
    [Fact]
    public void UserLine_ProducesNoControlCommand() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"user","message":{"role":"user","content":"你好"}}""");

        parsed.Control.Should().BeNull();
        parsed.Text.Should().Be("你好");
    }

    /// <summary>原始行 → 解析 → 控制面（解析必须产出命令，避免「解析器悄悄诊断掉」这类回归）。</summary>
    private static JoinCode.Cli.Output.SdkControlOutcome Send(
        string line, Func<string, string?>? applyPermissionMode = null) {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(line);
        var command = parsed.Control
                      ?? throw new InvalidOperationException($"控制行必须降成 SdkControlCommand：{line}");
        return JoinCode.Cli.Output.SdkControlPlane.CreateDefault(applyPermissionMode).Dispatch(command);
    }
}
