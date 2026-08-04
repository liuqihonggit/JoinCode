
namespace Core.Goal.Tests;

public sealed class SubAgentGraderTests
{
    [Fact]
    public void EvaluateRules_FailedExecution_ShouldReturnZero()
    {
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "",
            IsSuccess = false,
            Error = "crashed"
        };

        var result = SubAgentGrader.EvaluateRules(context);

        Assert.Equal(0.0, result.Score);
        Assert.Equal(GradingMethod.RuleFallback, result.Method);
        Assert.Contains(result.Criteria, c => c.Name == "execution" && c.Score == 0.0);
    }

    [Fact]
    public void EvaluateRules_SuccessWithCheckpointPassed_ShouldReturnHighScore()
    {
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "done",
            IsSuccess = true,
            CheckpointResult = CheckpointResult.Pass()
        };

        var result = SubAgentGrader.EvaluateRules(context);

        Assert.True(result.Score > 0.8);
        Assert.Equal(3, result.Criteria.Count);
    }

    [Fact]
    public void EvaluateRules_SuccessWithCheckpointFailed_ShouldReturnMidScore()
    {
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "done",
            IsSuccess = true,
            CheckpointResult = CheckpointResult.Fail([new CheckpointViolation { Rule = "build", Message = "编译失败", Severity = "error" }])
        };

        var result = SubAgentGrader.EvaluateRules(context);

        Assert.True(result.Score < 0.7);
        Assert.True(result.Score > 0.0);
    }

    [Fact]
    public void EvaluateRules_SuccessNoOutput_ShouldReturnMidScore()
    {
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "",
            IsSuccess = true
        };

        var result = SubAgentGrader.EvaluateRules(context);

        Assert.True(result.Score < 1.0);
    }

    [Fact]
    public void ParseGradingResult_ValidJson_ShouldParse()
    {
        var content = """{"reason": "good work", "criteria": [{"name": "correctness", "score": 0.9, "feedback": "correct"}, {"name": "completeness", "score": 0.8, "feedback": "mostly done"}, {"name": "quality", "score": 0.7, "feedback": "decent"}]}""";

        var result = SubAgentGrader.ParseGradingResult(content);

        Assert.NotNull(result);
        Assert.Equal(GradingMethod.LlmEvaluation, result.Method);
        Assert.Equal(3, result.Criteria.Count);
        Assert.True(result.Score > 0.7);
    }

    [Fact]
    public void ParseGradingResult_NullContent_ShouldReturnNull()
    {
        var result = SubAgentGrader.ParseGradingResult(null);

        Assert.Null(result);
    }

    [Fact]
    public void ParseGradingResult_EmptyContent_ShouldReturnNull()
    {
        var result = SubAgentGrader.ParseGradingResult("");

        Assert.Null(result);
    }

    [Fact]
    public void ParseGradingResult_InvalidJson_ShouldReturnNull()
    {
        var result = SubAgentGrader.ParseGradingResult("not json at all");

        Assert.Null(result);
    }

    [Fact]
    public void ParseGradingResult_JsonInCodeBlock_ShouldParse()
    {
        var content = "```json\n{\"reason\": \"ok\", \"criteria\": [{\"name\": \"correctness\", \"score\": 1.0, \"feedback\": \"perfect\"}]}\n```";

        var result = SubAgentGrader.ParseGradingResult(content);

        Assert.NotNull(result);
        Assert.Single(result.Criteria);
    }

    [Fact]
    public async Task GradeAsync_FailedExecution_ShouldUseRulesOnly()
    {
        var kernel = new Mock<IChatClient>();
        var grader = new SubAgentGrader(kernel.Object);

        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "",
            IsSuccess = false,
            Error = "failed"
        };

        var result = await grader.GradeAsync(context);

        Assert.Equal(0.0, result.Score);
        Assert.Equal(GradingMethod.RuleFallback, result.Method);
        kernel.Verify(k => k.GetChatCompletionService(), Times.Never);
    }

    [Fact]
    public async Task GradeAsync_LlmReturnsValid_ShouldUseLlmResult()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"reason": "good", "criteria": [{"name": "correctness", "score": 0.9, "feedback": "ok"}]}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var grader = new SubAgentGrader(kernel.Object);
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "done",
            IsSuccess = true
        };

        var result = await grader.GradeAsync(context);

        Assert.Equal(GradingMethod.LlmEvaluation, result.Method);
    }

    [Fact]
    public async Task GradeAsync_LlmThrows_ShouldFallbackToRules()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM error"));

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var grader = new SubAgentGrader(kernel.Object);
        var context = new GradingContext
        {
            AgentId = "a1",
            TaskDescription = "test",
            AgentOutput = "done",
            IsSuccess = true
        };

        var result = await grader.GradeAsync(context);

        Assert.Equal(GradingMethod.RuleFallback, result.Method);
        Assert.True(result.Score > 0.0);
    }

    [Fact]
    public async Task GradeAsync_NullContext_ShouldThrow()
    {
        var kernel = new Mock<IChatClient>();
        var grader = new SubAgentGrader(kernel.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => grader.GradeAsync(null!));
    }
}
