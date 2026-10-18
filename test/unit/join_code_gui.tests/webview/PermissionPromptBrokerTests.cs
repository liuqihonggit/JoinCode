namespace JoinCode.Gui.Tests.WebView;

/// <summary>
/// 权限确认弹窗代理测试（F2）：
/// ① 引擎请求 → permission/request 桥接消息（Id/工具/文案/规则/危险等级映射自 [EnumValue]）；
/// ② web 回传 permission/response → 三态决策（deny/allow/alwaysAllow）唤醒等待方；
/// ③ fail-closed 收口：超时、令牌取消、未知 Id、非法决策值、重复 Id 一律 Deny，且不抛到引擎。
/// </summary>
public sealed class PermissionPromptBrokerTests {
    private static PermissionConfirmationRequest CreateRequest(string? requestId = "req-1") => new(
        "bash",
        "[红灯ask] 是否允许执行 bash 命令？",
        requestId,
        "Bash(rm:*)") { DangerLevel = CommandDangerLevel.Execution };

    [Fact]
    public async Task RequestAsync_SendsPermissionRequest_WithMappedFields() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var pending = sut.RequestAsync(CreateRequest(), CancellationToken.None);
        var request = sentMessages.Single().Should().BeOfType<PermissionRequestMessage>().Subject;
        request.Id.Should().Be("req-1", "引擎给出的 RequestId 须沿用（便于日志串联）");
        request.Tool.Should().Be("bash");
        request.Prompt.Should().Contain("[红灯ask]");
        request.RuleContent.Should().Be("Bash(rm:*)");
        request.DangerLevel.Should().Be(CommandDangerLevel.Execution.ToValue(), "危险等级值须取自 [EnumValue]");
        sut.PendingCount.Should().Be(1, "应答前请求保持在途");

        sut.Resolve(new PermissionResponseMessage("req-1", PermissionConfirmationDecision.Deny.ToValue()));
        (await pending).Should().Be(PermissionConfirmationDecision.Deny);
        sut.PendingCount.Should().Be(0, "应答后出桶");
    }

    [Theory]
    [InlineData(PermissionConfirmationDecision.Allow)]
    [InlineData(PermissionConfirmationDecision.AlwaysAllow)]
    [InlineData(PermissionConfirmationDecision.Deny)]
    public async Task RequestAsync_ResolvedByWebDecision(PermissionConfirmationDecision expected) {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var pending = sut.RequestAsync(CreateRequest(), CancellationToken.None);
        sut.Resolve(new PermissionResponseMessage("req-1", expected.ToValue()));

        (await pending).Should().Be(expected, "三态决策须原样上浮给引擎（allow/alwaysAllow 由引擎批准工具）");
    }

    [Fact]
    public async Task RequestAsync_NoRequestId_GeneratesIdAndResolves() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var pending = sut.RequestAsync(CreateRequest(requestId: null), CancellationToken.None);
        var request = sentMessages.Single().Should().BeOfType<PermissionRequestMessage>().Subject;
        request.Id.Should().NotBeNullOrWhiteSpace("引擎未给 RequestId 时宿主生成，web 回传才能对上");
        sut.Resolve(new PermissionResponseMessage(request.Id, PermissionConfirmationDecision.Allow.ToValue()));

        (await pending).Should().Be(PermissionConfirmationDecision.Allow);
    }

    [Fact]
    public async Task RequestAsync_IllegalDecisionValue_FailsClosedToDeny() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var pending = sut.RequestAsync(CreateRequest(), CancellationToken.None);
        sut.Resolve(new PermissionResponseMessage("req-1", "yes-please"));

        (await pending).Should().Be(PermissionConfirmationDecision.Deny, "非法决策值不得被解释为放行");
    }

    [Fact]
    public async Task RequestAsync_UnknownIdResponse_IgnoredWithoutAffectingPending() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var pending = sut.RequestAsync(CreateRequest(), CancellationToken.None);
        sut.Resolve(new PermissionResponseMessage("other-request", PermissionConfirmationDecision.Allow.ToValue()));
        sut.PendingCount.Should().Be(1, "无匹配请求的应答不得消耗在途请求");

        sut.Resolve(new PermissionResponseMessage("req-1", PermissionConfirmationDecision.Allow.ToValue()));
        (await pending).Should().Be(PermissionConfirmationDecision.Allow);
    }

    [Fact]
    public async Task RequestAsync_DuplicateRequestId_FailsClosedToDeny() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var first = sut.RequestAsync(CreateRequest(), CancellationToken.None);
        var second = sut.RequestAsync(CreateRequest(), CancellationToken.None);

        (await second).Should().Be(PermissionConfirmationDecision.Deny, "同 Id 重复请求不得覆盖在途应答");
        sentMessages.Should().HaveCount(1, "冲突请求不下发（避免 web 侧同 Id 双弹窗）");

        sut.Resolve(new PermissionResponseMessage("req-1", PermissionConfirmationDecision.Allow.ToValue()));
        (await first).Should().Be(PermissionConfirmationDecision.Allow);
    }

    [Fact]
    public async Task RequestAsync_Timeout_FailsClosedToDeny() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add, TimeSpan.FromMilliseconds(30));

        var decision = await sut.RequestAsync(CreateRequest(), CancellationToken.None);

        decision.Should().Be(PermissionConfirmationDecision.Deny, "用户长时间无应答须按拒绝收口");
        sut.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestAsync_TokenCanceled_FailsClosedToDeny() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);
        using var cts = new CancellationTokenSource();

        var pending = sut.RequestAsync(CreateRequest(), cts.Token);
        cts.Cancel();

        (await pending).Should().Be(PermissionConfirmationDecision.Deny, "窗口关闭/看门狗取消须按拒绝收口");
        sut.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelAll_DeniesEveryPendingRequest() {
        var sentMessages = new List<BridgeMessage>();
        var sut = new PermissionPromptBroker(sentMessages.Add);

        var first = sut.RequestAsync(CreateRequest("req-1"), CancellationToken.None);
        var second = sut.RequestAsync(CreateRequest("req-2"), CancellationToken.None);
        sut.CancelAll();

        (await first).Should().Be(PermissionConfirmationDecision.Deny);
        (await second).Should().Be(PermissionConfirmationDecision.Deny);
        sut.PendingCount.Should().Be(0, "关窗不得留下悬挂的引擎等待");
    }

    [Fact]
    public void ParseDecision_ValuesFromEnum_OrDeny() {
        PermissionPromptBroker.ParseDecision(PermissionConfirmationDecision.Allow.ToValue()).Should().Be(PermissionConfirmationDecision.Allow);
        PermissionPromptBroker.ParseDecision(PermissionConfirmationDecision.AlwaysAllow.ToValue()).Should().Be(PermissionConfirmationDecision.AlwaysAllow);
        PermissionPromptBroker.ParseDecision("ALWAYSALLOW").Should().Be(PermissionConfirmationDecision.AlwaysAllow, "反查表大小写不敏感");
        PermissionPromptBroker.ParseDecision(null).Should().Be(PermissionConfirmationDecision.Deny);
        PermissionPromptBroker.ParseDecision("").Should().Be(PermissionConfirmationDecision.Deny);
        PermissionPromptBroker.ParseDecision("maybe").Should().Be(PermissionConfirmationDecision.Deny);
    }
}
