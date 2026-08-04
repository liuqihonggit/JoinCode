
namespace Core.Goal.Tests;

public sealed class ClusterResultSummarizerTests
{
    [Fact]
    public void BuildRuleBasedSummary_AllSuccess_ShouldListAll()
    {
        var summaries = new List<WorkerSummary>
        {
            new() { SubTaskId = "s1", Title = "Task A", Summary = "done", Score = 0.9 },
            new() { SubTaskId = "s2", Title = "Task B", Summary = "ok", Score = 0.8 },
        };

        var result = ClusterResultSummarizer.BuildRuleBasedSummary(summaries, []);

        Assert.Contains("2 成功", result);
        Assert.Contains("Task A", result);
        Assert.Contains("Task B", result);
    }

    [Fact]
    public void BuildRuleBasedSummary_WithFailures_ShouldListBoth()
    {
        var summaries = new List<WorkerSummary>
        {
            new() { SubTaskId = "s1", Title = "Task A", Summary = "done", Score = 1.0 },
        };
        var failures = new List<WorkerOutput>
        {
            new() { SubTaskId = "s2", Title = "Task B", Output = "", IsSuccess = false, GradingScore = 0.0 },
        };

        var result = ClusterResultSummarizer.BuildRuleBasedSummary(summaries, failures);

        Assert.Contains("1 成功", result);
        Assert.Contains("1 失败", result);
        Assert.Contains("Task B", result);
    }

    [Fact]
    public void BuildRuleBasedSummary_NoWorkers_ShouldShowZero()
    {
        var result = ClusterResultSummarizer.BuildRuleBasedSummary([], []);

        Assert.Contains("0 成功", result);
        Assert.Contains("0 失败", result);
    }

    [Fact]
    public async Task SummarizeAsync_NullContext_ShouldThrow()
    {
        var kernel = new Mock<IChatClient>();
        var sut = new ClusterResultSummarizer(kernel.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SummarizeAsync(null!));
    }

    [Fact]
    public async Task SummarizeAsync_LlmThrows_ShouldFallbackToRules()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM error"));
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var sut = new ClusterResultSummarizer(kernel.Object);
        var context = new ClusterSummaryContext
        {
            Objective = "test",
            WorkerOutputs = [new() { SubTaskId = "s1", Title = "A", Output = "done", IsSuccess = true, GradingScore = 0.9 }]
        };

        var result = await sut.SummarizeAsync(context);

        Assert.Contains("集群执行完成", result.Summary);
    }

    [Fact]
    public async Task SummarizeAsync_LlmReturnsValid_ShouldUseLlmSummary()
    {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "All tasks completed successfully." }]);
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var sut = new ClusterResultSummarizer(kernel.Object);
        var context = new ClusterSummaryContext
        {
            Objective = "test",
            WorkerOutputs = [new() { SubTaskId = "s1", Title = "A", Output = "done", IsSuccess = true, GradingScore = 1.0 }]
        };

        var result = await sut.SummarizeAsync(context);

        Assert.Equal("All tasks completed successfully.", result.Summary);
        Assert.Equal(1.0, result.OverallScore);
    }
}
