namespace Mcp.Tests;

/// <summary>
/// UserInteractionToolHandlers 单元测试 — 验证 confirm_action 工具
/// </summary>
public sealed class UserInteractionToolHandlersTests {
    [Fact]
    public async Task ConfirmActionAsync_EmptyAction_ReturnsError() {
        var mock = new MockInteractiveService();
        var handler = new UserInteractionToolHandlers(mock);

        var result = await handler.ConfirmActionAsync("");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("action cannot be empty");
    }

    [Fact]
    public async Task ConfirmActionAsync_UserConfirms_ReturnsConfirmedMessage() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.SuccessResult("是") };
        var handler = new UserInteractionToolHandlers(mock);

        var result = await handler.ConfirmActionAsync("delete file");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("confirmed");
        result.GetFirstText().Should().Contain("delete file");
    }

    [Fact]
    public async Task ConfirmActionAsync_UserDeclines_ReturnsDeclinedMessage() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.SuccessResult("否") };
        var handler = new UserInteractionToolHandlers(mock);

        var result = await handler.ConfirmActionAsync("delete file");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("declined");
        result.GetFirstText().Should().Contain("delete file");
    }

    [Fact]
    public async Task ConfirmActionAsync_UserCancels_ReturnsError() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.CancelledResult() };
        var handler = new UserInteractionToolHandlers(mock);

        var result = await handler.ConfirmActionAsync("delete file");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("cancelled");
    }

    [Fact]
    public async Task ConfirmActionAsync_Failure_ReturnsError() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.FailureResult("connection lost") };
        var handler = new UserInteractionToolHandlers(mock);

        var result = await handler.ConfirmActionAsync("delete file");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmActionAsync_WithMessage_UsesMessageAsQuestion() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.SuccessResult("是") };
        var handler = new UserInteractionToolHandlers(mock);

        await handler.ConfirmActionAsync("delete file", "Are you sure you want to delete this file?");

        mock.LastQuestion.Should().Be("Are you sure you want to delete this file?");
    }

    [Fact]
    public async Task ConfirmActionAsync_WithoutMessage_UsesActionAsQuestion() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.SuccessResult("是") };
        var handler = new UserInteractionToolHandlers(mock);

        await handler.ConfirmActionAsync("delete file");

        mock.LastQuestion.Should().Be("delete file");
    }

    [Fact]
    public async Task ConfirmActionAsync_PassesYesNoOptions() {
        var mock = new MockInteractiveService { ResultToReturn = AskUserQuestionResult.SuccessResult("是") };
        var handler = new UserInteractionToolHandlers(mock);

        await handler.ConfirmActionAsync("delete file");

        mock.LastOptions.Should().BeEquivalentTo(["是", "否"]);
    }

    private sealed class MockInteractiveService : IInteractiveService {
        public AskUserQuestionResult ResultToReturn { get; set; } = AskUserQuestionResult.SuccessResult("是");
        public string? LastQuestion { get; private set; }
        public List<string> LastOptions { get; private set; } = [];

        public Task<AskUserQuestionResult> AskUserQuestionAsync(string question, List<string>? options = null, bool multiSelect = false, CancellationToken cancellationToken = default) {
            LastQuestion = question;
            LastOptions = options ?? [];
            return Task.FromResult(ResultToReturn);
        }

        public Task<AskUserQuestionResult> AskUserQuestionsAsync(List<QuestionItem> questions, CancellationToken cancellationToken = default) {
            return Task.FromResult(ResultToReturn);
        }
    }
}
