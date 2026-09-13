namespace Core.Tests.Context;

/// <summary>
/// AppendOnlyLog 单元测试 — 验证撤回操作（TrimLastTurn, TruncateTo）的正确性
/// 和前缀缓存保持特性（撤回后剩余消息必须是原始消息的前缀）
/// </summary>
public sealed class AppendOnlyLogTests
{
    /// <summary>
    /// 构造包含多轮对话的 AppendOnlyLog：
    /// [User1, Assistant1, User2, Assistant2, User3, Assistant3]
    /// </summary>
    private static AppendOnlyLog BuildMultiTurnLog()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "用户消息1"));
        log.Append(new ApiMessage(MessageRole.Assistant, "助手回复1"));
        log.Append(new ApiMessage(MessageRole.User, "用户消息2"));
        log.Append(new ApiMessage(MessageRole.Assistant, "助手回复2"));
        log.Append(new ApiMessage(MessageRole.User, "用户消息3"));
        log.Append(new ApiMessage(MessageRole.Assistant, "助手回复3"));
        return log;
    }

    // === TrimLastTurn 测试 ===

    [Fact]
    public void TrimLastTurn_EmptyLog_ReturnsZero()
    {
        var log = new AppendOnlyLog();

        var removed = log.TrimLastTurn();

        removed.Should().Be(0);
        log.Count.Should().Be(0);
    }

    [Fact]
    public void TrimLastTurn_NoUserMessage_ReturnsZero()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.Assistant, "只有助手消息"));
        log.Append(new ApiMessage(MessageRole.Tool, "工具结果"));

        var removed = log.TrimLastTurn();

        removed.Should().Be(0);
        log.Count.Should().Be(2);
    }

    [Fact]
    public void TrimLastTurn_SingleUser_RemovesOne()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "单条用户消息"));

        var removed = log.TrimLastTurn();

        removed.Should().Be(1);
        log.Count.Should().Be(0);
    }

    [Fact]
    public void TrimLastTurn_UserAndAssistant_RemovesTwo()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "用户消息"));
        log.Append(new ApiMessage(MessageRole.Assistant, "助手回复"));

        var removed = log.TrimLastTurn();

        removed.Should().Be(2);
        log.Count.Should().Be(0);
    }

    [Fact]
    public void TrimLastTurn_MultiTurn_RemovesOnlyLastTurn()
    {
        var log = BuildMultiTurnLog();

        var removed = log.TrimLastTurn();

        removed.Should().Be(2);
        log.Count.Should().Be(4);
        log[0].Content.Should().Be("用户消息1");
        log[3].Content.Should().Be("助手回复2");
    }

    [Fact]
    public void TrimLastTurn_WithToolCalls_RemovesUserAndAllFollowers()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "读取文件"));
        log.Append(new ApiMessage(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
            [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])));
        log.Append(new ApiMessage(MessageRole.Tool, "文件内容", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")));
        log.Append(new ApiMessage(MessageRole.Assistant, "已读取文件"));

        var removed = log.TrimLastTurn();

        removed.Should().Be(4);
        log.Count.Should().Be(0);
    }

    // === TruncateTo 测试 ===

    [Fact]
    public void TruncateTo_Zero_RemovesAll()
    {
        var log = BuildMultiTurnLog();

        var removed = log.TruncateTo(0);

        removed.Should().Be(6);
        log.Count.Should().Be(0);
    }

    [Fact]
    public void TruncateTo_Count_RemovesNothing()
    {
        var log = BuildMultiTurnLog();

        var removed = log.TruncateTo(6);

        removed.Should().Be(0);
        log.Count.Should().Be(6);
    }

    [Fact]
    public void TruncateTo_Middle_RemovesTail()
    {
        var log = BuildMultiTurnLog();

        var removed = log.TruncateTo(3);

        removed.Should().Be(3);
        log.Count.Should().Be(3);
        log[0].Content.Should().Be("用户消息1");
        log[2].Content.Should().Be("用户消息2");
    }

    [Fact]
    public void TruncateTo_Negative_Throws()
    {
        var log = new AppendOnlyLog();

        var act = () => log.TruncateTo(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TruncateTo_ExceedsCount_Throws()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "msg"));

        var act = () => log.TruncateTo(5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // === 前缀缓存保持测试 ===

    /// <summary>
    /// 撤回后剩余消息必须是原始消息的前缀，否则前缀缓存会被破坏。
    /// 这是 /rewind 命令对前缀缓存影响的核心验证。
    /// </summary>
    [Fact]
    public void TrimLastTurn_RemainingMessagesArePrefixOfOriginal()
    {
        var log = BuildMultiTurnLog();
        var originalMessages = log.ToMessages().ToList();

        log.TrimLastTurn();

        var remainingMessages = log.ToMessages();

        remainingMessages.Count.Should().Be(4);
        for (var i = 0; i < remainingMessages.Count; i++)
        {
            remainingMessages[i].Role.Should().Be(originalMessages[i].Role);
            remainingMessages[i].Content.Should().Be(originalMessages[i].Content);
        }
    }

    [Fact]
    public void TruncateTo_RemainingMessagesArePrefixOfOriginal()
    {
        var log = BuildMultiTurnLog();
        var originalMessages = log.ToMessages().ToList();

        log.TruncateTo(3);

        var remainingMessages = log.ToMessages();

        remainingMessages.Count.Should().Be(3);
        for (var i = 0; i < remainingMessages.Count; i++)
        {
            remainingMessages[i].Role.Should().Be(originalMessages[i].Role);
            remainingMessages[i].Content.Should().Be(originalMessages[i].Content);
        }
    }

    [Fact]
    public void TrimLastTurn_PreservesMetadataInRemainingMessages()
    {
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "第一轮用户消息"));
        log.Append(new ApiMessage(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
            [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])));
        log.Append(new ApiMessage(MessageRole.Tool, "文件内容", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")));
        log.Append(new ApiMessage(MessageRole.Assistant, "已读取文件"));
        log.Append(new ApiMessage(MessageRole.User, "第二轮用户消息"));
        log.Append(new ApiMessage(MessageRole.Assistant, "第二轮回复"));

        log.TrimLastTurn();

        var remaining = log.ToMessages();
        remaining.Count.Should().Be(4);
        var assistantWithToolCalls = remaining.FirstOrDefault(m => m.Role == MessageRole.Assistant && m.Metadata is not null);
        assistantWithToolCalls.Should().NotBeNull();
        var extractedCalls = assistantWithToolCalls!.ExtractToolCalls();
        extractedCalls.Should().NotBeEmpty();
        extractedCalls[0].Id.Should().Be("call_001");
        extractedCalls[0].Name.Should().Be("Read");
    }
}
