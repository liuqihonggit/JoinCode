namespace McpToolDispatch.Tests;

/// <summary>
/// TASK135 B1 — <c>workflow_clear_history</c> 与 <c>/clear</c> 语义必须一致：
/// 会话翻页（换 sessionId、旧代留磁盘），而不是只清内存桶。
/// 只清内存的症状是模型对用户说「已清空」，下一次 resume 整段历史又回来。
/// </summary>
public sealed class WorkflowClearHistorySemanticsTests {
    private static WorkflowToolHandlers Create(IChatService chat) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                // 强制非提示词模式（否则工具走内存分支，测不到真实路径）
                ["McpServer:OperationMode"] = "aikey",
            })
            .Build();

        return new WorkflowToolHandlers(
            planService: null,
            chatService: chat,
            codeService: null,
            configuration: configuration);
    }

    [Fact]
    public async Task WorkflowClearHistory_StartsNewConversation_InsteadOfOnlyClearingMemory() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.StartNewConversationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("s-new-epoch");

        var handlers = Create(chat.Object);

        var result = await handlers.WorkflowClearHistoryAsync().ConfigureAwait(true);

        result.Should().NotBeNull();
        chat.Verify(
            c => c.StartNewConversationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "工具路径必须走会话翻页，否则磁盘 transcript 还在、resume 会把历史带回来");
        chat.Verify(
            c => c.ClearHistoryAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "只清内存是本次要修的缺陷，不得再走这条路");
    }

    [Fact]
    public async Task WorkflowClearHistory_WhenChatServiceMissing_ReturnsError() {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["McpServer:OperationMode"] = "aikey",
            })
            .Build();
        var handlers = new WorkflowToolHandlers(null, null, null, configuration);

        var result = await handlers.WorkflowClearHistoryAsync().ConfigureAwait(true);

        result.IsError.Should().BeTrue("没有聊天服务时不能假装清空成功");
    }
}
