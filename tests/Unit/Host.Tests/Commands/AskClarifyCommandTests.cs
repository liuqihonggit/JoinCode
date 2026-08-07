namespace Core.Tests.Commands;

/// <summary>
/// /?? 需求澄清命令单元测试
/// </summary>
public class AskClarifyCommandTests
{
    [Fact]
    public void Command_ShouldHaveCorrectName()
    {
        var command = new AskClarifyCommand();

        command.Name.Should().Be(ChatCommandNameConstants.AskClarify);
        command.Name.Should().Be("??");
    }

    [Fact]
    public void Command_ShouldHaveAskAlias()
    {
        var command = new AskClarifyCommand();

        command.Aliases.Should().Contain("ask");
    }

    [Fact]
    public void Command_ShouldHaveNonEmptyDescription()
    {
        var command = new AskClarifyCommand();

        command.Description.Should().NotBeEmpty();
        command.Description.Should().Contain("澄清");
    }

    [Fact]
    public void Command_ShouldHaveCorrectCategory()
    {
        var command = new AskClarifyCommand();

        var attr = typeof(AskClarifyCommand).GetCustomAttributes(typeof(ChatCommandAttribute), false)
            .Cast<ChatCommandAttribute>().First();
        attr.Category.Should().Be(ChatCommandCategory.Info);
    }

    [Fact]
    public void ClarifyDoneMarker_ShouldBeExpectedValue()
    {
        AskClarifyCommand.ClarifyDoneMarker.Should().Be("【需求已明确】");
    }

    [Theory]
    [InlineData("/end")]
    [InlineData("/done")]
    [InlineData("/exit")]
    [InlineData("/quit")]
    [InlineData("/END")]
    [InlineData("  /end  ")]
    public void IsExitCommand_ShouldReturnTrue_ForExitCommands(string input)
    {
        AskClarifyCommand.IsExitCommand(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("/help")]
    [InlineData("/ask")]
    [InlineData("继续")]
    public void IsExitCommand_ShouldReturnFalse_ForNonExitCommands(string input)
    {
        AskClarifyCommand.IsExitCommand(input).Should().BeFalse();
    }

    [Fact]
    public void SystemPrompt_ShouldNotBeEmpty()
    {
        AskClarifyPrompts.SystemPrompt.Should().NotBeEmpty();
    }

    [Fact]
    public void SystemPrompt_ShouldContainKeyPhrases()
    {
        var prompt = AskClarifyPrompts.SystemPrompt;

        prompt.Should().Contain("需求澄清模式");
        prompt.Should().Contain("选择题");
        prompt.Should().Contain("鱼骨图");
        prompt.Should().Contain("产品经理");
        prompt.Should().Contain("AskUserQuestion");
        prompt.Should().Contain("新手");
    }

    [Fact]
    public void SystemPrompt_ShouldContainClarifyRules()
    {
        var prompt = AskClarifyPrompts.SystemPrompt;

        prompt.Should().Contain("【澄清规则】");
        prompt.Should().Contain("【需求已明确】");
    }
}

/// <summary>
/// TerminalInteractiveService 单元测试 — 仅测试不涉及终端 I/O 的逻辑
/// </summary>
public class TerminalInteractiveServiceTests
{
    [Fact]
    public async Task AskUserQuestionAsync_EmptyQuestion_ShouldReturnFailure()
    {
        var service = new TerminalInteractiveService();

        var result = await service.AskUserQuestionAsync("");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AskUserQuestionsAsync_EmptyList_ShouldReturnFailure()
    {
        var service = new TerminalInteractiveService();

        var result = await service.AskUserQuestionsAsync([]);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AskUserQuestionsAsync_TooManyQuestions_ShouldReturnFailure()
    {
        var service = new TerminalInteractiveService();
        var questions = Enumerable.Range(0, 5).Select(i => new QuestionItem
        {
            Question = $"Q{i}",
            Header = $"H{i}",
            Options = [new() { Label = "A", Description = "a" }, new() { Label = "B", Description = "b" }]
        }).ToList();

        var result = await service.AskUserQuestionsAsync(questions);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Maximum");
    }

    [Fact]
    public async Task AskUserQuestionsAsync_InvalidOptionCount_ShouldReturnFailure()
    {
        var service = new TerminalInteractiveService();
        var questions = new List<QuestionItem>
        {
            new()
            {
                Question = "Q1",
                Header = "H1",
                Options = [new() { Label = "A", Description = "a" }]
            }
        };

        var result = await service.AskUserQuestionsAsync(questions);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("2-4 options");
    }

    [Fact]
    public async Task AskUserQuestionsAsync_DuplicateLabels_ShouldReturnFailure()
    {
        var service = new TerminalInteractiveService();
        var questions = new List<QuestionItem>
        {
            new()
            {
                Question = "Q1",
                Header = "H1",
                Options =
                [
                    new() { Label = "A", Description = "a" },
                    new() { Label = "A", Description = "b" }
                ]
            }
        };

        var result = await service.AskUserQuestionsAsync(questions);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("duplicate");
    }
}
