namespace Host.Tests.Tui;

/// <summary>
/// TerminalGuiInteractiveService 校验测试 — TUI ask_user_question 服务层的输入校验与未就绪兜底。
/// 对齐 CLI TerminalInteractiveService 的校验语义（空问题/最多4问/选项数2-4/重复标签）。
/// </summary>
public class TerminalGuiInteractiveServiceTests
{
    private static QuestionItem MakeQuestion(string question = "选一个?", List<QuestionOption>? options = null) => new()
    {
        Header = "测试",
        Question = question,
        Options = options ?? [new() { Label = "A", Description = "" }, new() { Label = "B", Description = "" }],
    };

    [Fact]
    public async Task AskUserQuestion_EmptyQuestion_Fails()
    {
        var service = new TerminalGuiInteractiveService();
        var result = await service.AskUserQuestionAsync("   ");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task BeforeAttach_ReturnsFailureNotMockAnswer()
    {
        // 未绑定 UI 通道时必须显式失败 — 绝不能像 Mock 那样静默替用户作答
        var service = new TerminalGuiInteractiveService();
        var result = await service.AskUserQuestionAsync("问题", ["A", "B"]);
        Assert.False(result.Success);
        Assert.Contains("未就绪", result.ErrorMessage);
    }

    [Fact]
    public async Task AskUserQuestions_EmptyList_Fails()
    {
        var service = new TerminalGuiInteractiveService();
        var result = await service.AskUserQuestionsAsync([]);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task AskUserQuestions_MoreThanFour_Fails()
    {
        var service = new TerminalGuiInteractiveService();
        var questions = Enumerable.Range(0, 5).Select(_ => MakeQuestion()).ToList();
        var result = await service.AskUserQuestionsAsync(questions);
        Assert.False(result.Success);
        Assert.Contains("Maximum 4", result.ErrorMessage);
    }

    [Fact]
    public async Task AskUserQuestions_TooFewOptions_Fails()
    {
        var service = new TerminalGuiInteractiveService();
        var result = await service.AskUserQuestionsAsync(
            [MakeQuestion(options: [new QuestionOption { Label = "仅一个", Description = "" }])]);
        Assert.False(result.Success);
        Assert.Contains("2-4 options", result.ErrorMessage);
    }

    [Fact]
    public async Task AskUserQuestions_DuplicateLabels_Fail()
    {
        var service = new TerminalGuiInteractiveService();
        var result = await service.AskUserQuestionsAsync(
            [MakeQuestion(options:
            [
                new QuestionOption { Label = "相同", Description = "" },
                new QuestionOption { Label = "相同", Description = "" },
            ])]);
        Assert.False(result.Success);
        Assert.Contains("duplicate option labels", result.ErrorMessage);
    }
}
