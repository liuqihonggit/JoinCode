
namespace Core.Goal.Tests;

public sealed class GoalEvaluatorTests
{
    [Fact]
    public void ParseEvaluationResult_Json_Completed_True_Should_Return_Completed()
    {
        var content = """{"completed": true, "reason": "所有功能已实现"}""";

        var result = GoalEvaluator.ParseEvaluationResult(content);

        Assert.True(result.IsCompleted);
        Assert.Equal("所有功能已实现", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Json_Completed_False_Should_Return_NotCompleted()
    {
        var content = """{"completed": false, "reason": "仍有未完成的工作"}""";

        var result = GoalEvaluator.ParseEvaluationResult(content);

        Assert.False(result.IsCompleted);
        Assert.Equal("仍有未完成的工作", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Yes_Prefix_Should_Return_Completed()
    {
        var result = GoalEvaluator.ParseEvaluationResult("yes, the objective has been achieved");

        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void ParseEvaluationResult_No_Prefix_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult("no, still working on it");

        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void ParseEvaluationResult_Null_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult(null);

        Assert.False(result.IsCompleted);
        Assert.Equal("评估器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Empty_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult("");

        Assert.False(result.IsCompleted);
        Assert.Equal("评估器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Whitespace_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult("   ");

        Assert.False(result.IsCompleted);
        Assert.Equal("评估器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Unknown_Format_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult("maybe it's done");

        Assert.False(result.IsCompleted);
        Assert.Contains("格式异常", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Json_With_1_And_0_Should_Fallback_To_Text()
    {
        // System.Text.Json 不接受 1/0 作为 bool，JSON 解析失败后回退到文本格式
        var result1 = GoalEvaluator.ParseEvaluationResult("""{"completed": 1, "reason": "done"}""");
        var result0 = GoalEvaluator.ParseEvaluationResult("""{"completed": 0, "reason": "not done"}""");

        // JSON 解析失败 → 不匹配 yes/no 前缀 → 返回格式异常
        Assert.False(result1.IsCompleted);
        Assert.False(result0.IsCompleted);
    }

    [Fact]
    public void ParseEvaluationResult_Json_Without_Reason_Should_Use_Empty_Default()
    {
        // reason 字段有默认值 string.Empty，缺少时反序列化成功，Reason 为空字符串
        var result = GoalEvaluator.ParseEvaluationResult("""{"completed": true}""");

        Assert.True(result.IsCompleted);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Json_With_Malformed_Reason_Should_Fallback()
    {
        // reason 为数字 123 时类型不匹配，JSON 解析失败回退到文本格式
        var result = GoalEvaluator.ParseEvaluationResult("""{"completed": false, "reason": 123}""");

        Assert.False(result.IsCompleted);
        Assert.Contains("格式异常", result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_NullObjective_Should_Throw()
    {
        var kernel = new Mock<IChatClient>();
        var evaluator = new GoalEvaluator(kernel.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            evaluator.EvaluateAsync(null!, [], "conversation")).ConfigureAwait(true);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyObjective_Should_Throw()
    {
        var kernel = new Mock<IChatClient>();
        var evaluator = new GoalEvaluator(kernel.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            evaluator.EvaluateAsync("", [], "conversation")).ConfigureAwait(true);
    }

    [Fact]
    public async Task EvaluateAsync_ChatServiceThrows_Should_Return_NotCompleted()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("网络错误"));

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var evaluator = new GoalEvaluator(kernel.Object);
        var result = await evaluator.EvaluateAsync("实现功能", [], "对话内容").ConfigureAwait(true);

        Assert.False(result.IsCompleted);
        Assert.Equal("评估器不可用", result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_ChatServiceReturnsValidJson_Should_Parse()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"completed": true, "reason": "功能已实现"}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var evaluator = new GoalEvaluator(kernel.Object);
        var result = await evaluator.EvaluateAsync("实现功能", ["不修改API"], "对话内容").ConfigureAwait(true);

        Assert.True(result.IsCompleted);
        Assert.Equal("功能已实现", result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_ChatServiceReturnsEmpty_Should_Return_NotCompleted()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var evaluator = new GoalEvaluator(kernel.Object);
        var result = await evaluator.EvaluateAsync("实现功能", [], "对话内容").ConfigureAwait(true);

        Assert.False(result.IsCompleted);
        Assert.Equal("评估器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Yes_CaseInsensitive_Should_Return_Completed()
    {
        var result = GoalEvaluator.ParseEvaluationResult("YES, done");
        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void ParseEvaluationResult_No_CaseInsensitive_Should_Return_NotCompleted()
    {
        var result = GoalEvaluator.ParseEvaluationResult("NO, not yet");
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void ParseEvaluationResult_Json_With_Extra_Fields_Should_Parse()
    {
        var content = """{"completed": true, "reason": "done", "confidence": 0.95}""";
        var result = GoalEvaluator.ParseEvaluationResult(content);

        Assert.True(result.IsCompleted);
        Assert.Equal("done", result.Reason);
    }

    [Fact]
    public void ParseEvaluationResult_Json_True_Capitalized_Should_Fallback()
    {
        // True 大写不是合法 JSON bool，解析失败后回退到文本格式
        var content = """{"completed": True, "reason": "done"}""";
        var result = GoalEvaluator.ParseEvaluationResult(content);

        // JSON 解析失败 → 不匹配 yes/no → 返回格式异常
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public async Task EvaluateAsync_WithLogger_Should_Not_Throw()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("test error"));

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var logger = new Mock<ILogger<GoalEvaluator>>();
        var evaluator = new GoalEvaluator(kernel.Object, logger.Object);
        var result = await evaluator.EvaluateAsync("实现功能", [], "对话内容").ConfigureAwait(true);

        Assert.False(result.IsCompleted);
    }

    [Fact]
    public async Task EvaluateAsync_Should_Pass_Constraints_To_Prompt()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();

        MessageList? capturedHistory = null;
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"completed": false, "reason": "not done"}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var evaluator = new GoalEvaluator(kernel.Object);
        await evaluator.EvaluateAsync("实现功能", ["不修改公共API"], "对话内容").ConfigureAwait(true);

        Assert.NotNull(capturedHistory);
        Assert.True(capturedHistory.Count >= 2);

        var systemMessage = capturedHistory[0];
        Assert.Equal(MessageRole.System, systemMessage.Role);
        Assert.Contains("不修改公共API", systemMessage.Content);
    }
}
