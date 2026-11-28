namespace Host.Tests.Adapters;

/// <summary>
/// TASK144 — SDK 控制面骨架：锁住「带 request_id 必回包」这条协议不变量，以及
/// interrupt / set_permission_mode 两个参照实现与现网（TASK142-B/143）语义一致。
/// <para>本轮骨架**未接线**（<c>NonInteractiveModeRunner</c> 未改），因此这里不测宿主循环，
/// 只测控制面本身；真机验收留到 Phase 1 接线后按 TASK143 §5 口径做。</para>
/// </summary>
public sealed class SdkControlPlaneTests {
    private const string PermissionModeSubtype = "set_permission_mode";

    /// <summary>interrupt 必须回空载荷 success，并要求调用方取消在途回合。</summary>
    [Fact]
    public void Interrupt_AcknowledgesAndRequestsCancel() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        var outcome = plane.Dispatch(Command("r-int", "interrupt"));

        outcome.CancelActiveTurn.Should().BeTrue();
        var body = Body(outcome);
        body.Subtype.Should().Be("success");
        body.RequestId.Should().Be("r-int");
        body.Response.Should().BeEmpty("interrupt 无返回值，对齐基准 response:{}");
        body.Error.Should().BeNull();
    }

    /// <summary>
    /// 未实现 subtype 必须回 error 并带回 request_id —— 这是 TASK143 真机打回过的坑：
    /// 只写 stderr 会让客户端等一条永远不会来的应答。
    /// </summary>
    [Fact]
    public void UnsupportedSubtype_RepliesErrorWithSameRequestId() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        var outcome = plane.Dispatch(Command("r-model", "set_model"));

        var body = Body(outcome);
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("r-model", "不回原 ID 客户端无法关联");
        body.Error.Should().Contain("set_model");
        body.Error.Should().Contain("interrupt", "拒绝原因要列出当前支持什么");
        body.Error.Should().Contain(PermissionModeSubtype);
        outcome.Diagnostic.Should().Be(body.Error, "拒绝原因同时要进 stderr 便于本地排查");
    }

    /// <summary>缺 subtype 也要回 error（同样带 request_id），不能静默跳过。</summary>
    [Fact]
    public void MissingSubtype_RepliesError() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        var outcome = plane.Dispatch(new JoinCode.Cli.Output.SdkControlCommand {
            RequestId = "r-empty",
            Subtype = "   ",
        });

        var body = Body(outcome);
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("r-empty");
        body.Error.Should().Contain("subtype");
    }

    /// <summary>
    /// 没有 request_id 就无从回包：控制面直接拒绝这种命令（解析阶段应丢弃），
    /// 而不是产生一条没有 request_id 的应答把协议流弄脏。
    /// </summary>
    [Fact]
    public void Dispatch_WithoutRequestId_Throws() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        var act = () => plane.Dispatch(new JoinCode.Cli.Output.SdkControlCommand {
            RequestId = " ",
            Subtype = "interrupt",
        });

        act.Should().Throw<ArgumentException>().WithParameterName("command");
    }

    /// <summary>handler 抛异常也必须回包 —— 兜底在控制面，不在每个调用点。</summary>
    [Fact]
    public void HandlerThrows_StillRepliesError() {
        var plane = new JoinCode.Cli.Output.SdkControlPlane([new ThrowingHandler()]);

        var outcome = plane.Dispatch(Command("r-boom", "boom"));

        var body = Body(outcome);
        body.Subtype.Should().Be("error");
        body.RequestId.Should().Be("r-boom");
        body.Error.Should().Contain("永久等待", "错误文案要解释为什么必须回包");
    }

    /// <summary>同一 subtype 重复注册属于装配错误，构造期 fail-fast，避免「谁生效看顺序」。</summary>
    [Fact]
    public void DuplicateSubtype_Throws() {
        var act = () => new JoinCode.Cli.Output.SdkControlPlane([
            new JoinCode.Cli.Output.InterruptControlHandler(),
            new JoinCode.Cli.Output.InterruptControlHandler(),
        ]);

        act.Should().Throw<ArgumentException>().WithMessage("*重复注册*");
    }

    /// <summary>支持清单按注册顺序，且扩一个 handler 即扩清单（清单与能力同源）。</summary>
    [Fact]
    public void SupportedSubtypes_FollowRegistrationOrder() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        plane.SupportedSubtypes.Should().Equal("interrupt", PermissionModeSubtype);
    }

    /// <summary>
    /// set_permission_mode 成功时必须回**实际生效值**：bypass 可能被 settings.json 安全闸拦成别的模式，
    /// 回请求值会让客户端误判权限已放宽（与 bridge_serve 的 permission_mode_changed 同语义）。
    /// </summary>
    [Fact]
    public void SetPermissionMode_ReturnsEffectiveValueNotRequested() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(mode => mode == "bypass" ? "ask" : mode);

        var outcome = plane.Dispatch(Command("r-pm", PermissionModeSubtype, mode: "bypass"));

        var body = Body(outcome);
        body.Subtype.Should().Be("success");
        body.Response.Should().ContainKey("mode").WhoseValue.Should().Be("ask", "bypass 被安全闸拦下后实际是 ask");
    }

    /// <summary>缺 mode 回 error 并列出合法值（客户端据此修正，而不是猜）。</summary>
    [Fact]
    public void SetPermissionMode_MissingMode_RepliesErrorWithValidValues() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(mode => mode);

        var outcome = plane.Dispatch(Command("r-pm2", PermissionModeSubtype));

        var body = Body(outcome);
        body.Subtype.Should().Be("error");
        body.Error.Should().Contain(PermissionModeApplierFactory.ValidPermissionModes);
    }

    /// <summary>未知模式回 error（应用器返回 null），文案与 bridge_serve 共用同一份。</summary>
    [Fact]
    public void SetPermissionMode_UnknownMode_RepliesSharedFailureText() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(mode => mode == "plan" ? mode : null);

        var outcome = plane.Dispatch(Command("r-pm3", PermissionModeSubtype, mode: "bogus"));

        Body(outcome).Error.Should().Be(PermissionModeApplierFactory.DescribeFailure("bogus"));
    }

    /// <summary>引擎没有权限系统（应用器为 null）时回 error，不假装成功。</summary>
    [Fact]
    public void SetPermissionMode_NoPermissionSystem_RepliesError() {
        var plane = JoinCode.Cli.Output.SdkControlPlane.CreateDefault(null);

        var outcome = plane.Dispatch(Command("r-pm4", PermissionModeSubtype, mode: "plan"));

        var body = Body(outcome);
        body.Subtype.Should().Be("error");
        body.Error.Should().Contain("无法应用");
    }

    /// <summary>扩展点可用性：注册一个自定义 handler 即可接管新 subtype，无需改控制面。</summary>
    [Fact]
    public void CustomHandler_IsDispatchedBySubtype() {
        var plane = new JoinCode.Cli.Output.SdkControlPlane([new EchoHandler()]);

        var outcome = plane.Dispatch(Command("r-echo", "echo"));

        var body = Body(outcome);
        body.Subtype.Should().Be("success");
        body.Response.Should().ContainKey("echo").WhoseValue.Should().Be("r-echo");
    }

    /// <summary>
    /// 构造命令：payload 是 <c>request</c> 对象原名（subtype 必带，mode 可选）。
    /// <para>用 <c>JsonObject</c> 结构化构造再序列化，不手写 JSON 字符串（JCC1017）。</para>
    /// </summary>
    private static JoinCode.Cli.Output.SdkControlCommand Command(
        string requestId, string subtype, string? mode = null) {
        var payload = new System.Text.Json.Nodes.JsonObject { ["subtype"] = subtype };
        if (mode is not null)
            payload["mode"] = mode;
        using var document = System.Text.Json.JsonDocument.Parse(payload.ToJsonString());
        return new JoinCode.Cli.Output.SdkControlCommand {
            RequestId = requestId,
            Subtype = subtype,
            Payload = document.RootElement.Clone(), // Clone 脱离 document 生命周期，避免 use-after-dispose
        };
    }

    private static JoinCode.Cli.Output.SdkStreamControlResponseBody Body(JoinCode.Cli.Output.SdkControlOutcome outcome) =>
        outcome.Response.Response;

    /// <summary>模拟一个处理时抛异常的 handler（防御性兜底用）。</summary>
    private sealed class ThrowingHandler : JoinCode.Cli.Output.ISdkControlHandler {
        public string Subtype => "boom";

        public JoinCode.Cli.Output.SdkControlOutcome Handle(JoinCode.Cli.Output.SdkControlCommand command) =>
            throw new InvalidOperationException("模拟 handler 内部故障");
    }

    /// <summary>模拟一个只回自定义载荷的 handler（验证扩展点）。</summary>
    private sealed class EchoHandler : JoinCode.Cli.Output.ISdkControlHandler {
        public string Subtype => "echo";

        public JoinCode.Cli.Output.SdkControlOutcome Handle(JoinCode.Cli.Output.SdkControlCommand command) => new() {
            Response = new JoinCode.Cli.Output.SdkStreamControlResponse {
                Response = new JoinCode.Cli.Output.SdkStreamControlResponseBody {
                    Subtype = "success",
                    RequestId = command.RequestId,
                    Response = new Dictionary<string, string> { ["echo"] = command.RequestId },
                },
            },
        };
    }
}
