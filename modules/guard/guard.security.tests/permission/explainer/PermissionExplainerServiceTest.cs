namespace Core.Tests.Permission;

/// <summary>
/// 权限解释器薄壳（TASK069-P4）— 只测「决策与收场」：开关关闭不调用、无模型返回 null、
/// 正常响应转成四段、响应不可解析返回 null、底层抛异常一律吞成 null。
/// <para>「异常也不许上抛」是这一层的硬约束：解释只是帮用户下判断，任何失败都不能把权限确认拖下水。</para>
/// </summary>
public sealed class PermissionExplainerServiceTest {
    private static readonly PermissionExplainRequest Request = new("Bash", "需要确认: rm -rf /tmp/x", "执行 shell 命令");

    private static PermissionExplainerService CreateService(
        IQueryService? queryService,
        bool enabled = true,
        IChatService? chatService = null) =>
        new(queryService, chatService, new WorkflowConfig { PermissionExplainerEnabled = enabled });

    private static Mock<IQueryService> CreateQueryService(params ApiMessage[] response) {
        var mock = new Mock<IQueryService>();
        mock.Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return mock;
    }

    private static ApiMessage Text(string content) =>
        new() { Role = MessageRole.Assistant, Content = content };

    /// <summary>正常响应 → 四段齐全，等级从基准字符串反查。</summary>
    [Fact]
    public async Task ExplainAsync_ValidResponse_ReturnsExplanation() {
        var query = CreateQueryService(Text(
            """{"explanation":"删除临时目录","reasoning":"我需要清理构建残留","risk":"可能误删未提交内容","riskLevel":"MEDIUM"}"""));

        var result = await CreateService(query.Object).ExplainAsync(Request);

        var explanation = result.Should().BeOfType<PermissionExplanation>().Subject;
        explanation.RiskLevel.Should().Be(PermissionRiskLevel.Medium);
        explanation.Explanation.Should().Be("删除临时目录");
    }

    /// <summary>开关关闭 → 直接 null，且根本不打模型（省 token 是这一档存在的理由）。</summary>
    [Fact]
    public async Task ExplainAsync_Disabled_DoesNotCallModel() {
        var query = CreateQueryService(Text("""{"explanation":"a","reasoning":"b","risk":"c","riskLevel":"LOW"}"""));

        var result = await CreateService(query.Object, enabled: false).ExplainAsync(Request);

        result.Should().BeNull();
        query.Verify(q => q.GetApiMessageContentsAsync(
            It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>宿主没有 LLM（未注册 IQueryService）→ null，不抛。</summary>
    [Fact]
    public async Task ExplainAsync_NoQueryService_ReturnsNull() {
        var result = await CreateService(queryService: null).ExplainAsync(Request);

        result.Should().BeNull();
    }

    /// <summary>模型答歪 → null（调用方渲染「解释不可用」）。</summary>
    [Fact]
    public async Task ExplainAsync_GarbageResponse_ReturnsNull() {
        var query = CreateQueryService(Text("我无法分析这条命令"));

        var result = await CreateService(query.Object).ExplainAsync(Request);

        result.Should().BeNull();
    }

    /// <summary>底层抛异常 → 吞成 null（解释失败绝不能冒泡到权限确认路径）。</summary>
    [Fact]
    public async Task ExplainAsync_ProviderThrows_ReturnsNull() {
        var query = new Mock<IQueryService>();
        query.Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        var result = await CreateService(query.Object).ExplainAsync(Request);

        result.Should().BeNull();
    }

    /// <summary>调用方取消（收起对话框）→ null，不抛 OperationCanceledException。</summary>
    [Fact]
    public async Task ExplainAsync_Canceled_ReturnsNull() {
        var query = new Mock<IQueryService>();
        query.Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cts = new CancellationTokenSource();

        var result = await CreateService(query.Object).ExplainAsync(Request, cts.Token);

        result.Should().BeNull();
    }

    /// <summary>会话消息可用时进提示词（上下文是「我为什么跑它」的唯一来源）。</summary>
    [Fact]
    public async Task ExplainAsync_WithChatService_FeedsRecentAssistantContext() {
        MessageList? captured = null;
        var query = new Mock<IQueryService>();
        query.Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, IChatClient?, CancellationToken>((messages, _, _, _) => captured = messages)
            .ReturnsAsync([Text("""{"explanation":"a","reasoning":"b","risk":"c","riskLevel":"LOW"}""")]);

        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessageRecord { Role = "assistant", Content = "我先看看构建目录" }]);

        await CreateService(query.Object, chatService: chat.Object).ExplainAsync(Request);

        var sent = captured.Should().BeOfType<MessageList>().Subject;
        sent.Should().Contain(m => m.Content != null && m.Content.Contains("我先看看构建目录", StringComparison.Ordinal));
    }
}
