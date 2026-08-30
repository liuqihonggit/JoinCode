namespace Host.Tests.Tui.Views;

/// <summary>
/// AskUserDialogView 单元测试 — 验证 T2 ask_user_question 对话框渲染/提交/取消行为。
/// 回归背景：TUI DI 不含 CliModule，AskUserQuestion 工具此前走 Core Mock，用户从未被提问。
/// </summary>
public class AskUserDialogViewTests
{
    private static readonly QuestionItem SampleQuestion = new()
    {
        Header = "方案选择",
        Question = "使用哪种实现?",
        Options =
        [
            new QuestionOption { Label = "方案A", Description = "保守" },
            new QuestionOption { Label = "方案B", Description = "激进" },
            new QuestionOption { Label = "方案C", Description = "" },
        ],
        MultiSelect = false,
    };

    [Fact]
    public void Initial_Invisible()
    {
        var dialog = new AskUserDialogView();
        Assert.False(dialog.TerminalView.Visible);
    }

    [Fact]
    public async Task ShowAsync_RendersHeaderQuestionAndOptions()
    {
        var dialog = new AskUserDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync(SampleQuestion, cts.Token);

        Assert.True(dialog.TerminalView.Visible);
        var snapshot = ViewTreeSerializer.Serialize(dialog.TerminalView);
        Assert.Contains("方案选择", snapshot);
        Assert.Contains("使用哪种实现?", snapshot);
        Assert.Contains("方案B", snapshot);

        cts.Cancel();
        await task;
    }

    [Fact]
    public async Task Submit_SingleSelectValidIndex_CompletesWithAnswer()
    {
        var dialog = new AskUserDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync(SampleQuestion, cts.Token);
        SetInput(dialog, "2");

        InvokeSubmit(dialog);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(result.Success);
        Assert.Equal("方案B", result.Answer);
        Assert.False(dialog.TerminalView.Visible);
    }

    [Fact]
    public async Task Submit_Zero_Cancels()
    {
        var dialog = new AskUserDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync(SampleQuestion, cts.Token);
        SetInput(dialog, "0");

        InvokeSubmit(dialog);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(result.Success);
        Assert.True(result.Cancelled);
    }

    [Fact]
    public void Submit_InvalidIndex_KeepsDialogOpenForRetry()
    {
        // 无效输入不关窗 — 对齐 CLI 重试提示语义
        var dialog = new AskUserDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync(SampleQuestion, cts.Token);
        SetInput(dialog, "99");

        InvokeSubmit(dialog);

        Assert.False(task.IsCompleted, "无效输入不应完成应答");
        Assert.True(dialog.TerminalView.Visible);
        cts.Cancel();
    }

    [Fact]
    public async Task Submit_FreeInputWithoutOptions_CompletesWithText()
    {
        var dialog = new AskUserDialogView();
        using var cts = new CancellationTokenSource();
        var freeQuestion = new QuestionItem { Header = "补充", Question = "还有什么要求?", Options = [], MultiSelect = false };
        var task = dialog.ShowAsync(freeQuestion, cts.Token);
        SetInput(dialog, "尽量简洁");

        InvokeSubmit(dialog);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(result.Success);
        Assert.Equal("尽量简洁", result.Answer);
    }

    private static void SetInput(AskUserDialogView dialog, string text)
    {
        var field = typeof(AskUserDialogView)
            .GetField("_inputField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog) as TextField;
        field!.Text = text;
    }

    private static void InvokeSubmit(AskUserDialogView dialog)
    {
        var method = typeof(AskUserDialogView).GetMethod(
            "OnSubmit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(dialog, [dialog, EventArgs.Empty]);
    }
}
