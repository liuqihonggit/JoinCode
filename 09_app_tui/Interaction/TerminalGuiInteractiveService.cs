namespace JoinCode.Tui.Interaction;

/// <summary>
/// Terminal.Gui 交互服务 — MCP ask_user_question 工具的 TUI 真实实现，
/// 替代 Core 层 Mock（TUI DI 不含 CliModule，Mock 会让用户从未被真正提问）。
/// 对齐 GUI AvaloniaInteractiveService 的线程模型：工具线程阻塞等待，
/// UI 操作经 painter.Invoke 切到 Terminal.Gui 主循环。
/// </summary>
public sealed class TerminalGuiInteractiveService : IInteractiveService
{
    private TerminalPainter? _painter;

    private AskUserDialogView? _dialogView;

    /// <summary>
    /// 绑定 TUI 运行期实例 — TuiModeRunner 启动时调用；未绑定前提问返回失败（不可交互）。
    /// </summary>
    public void Attach(TerminalPainter painter, AskUserDialogView dialogView)
    {
        _painter = painter;
        _dialogView = dialogView;
    }

    /// <inheritdoc />
    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(AskUserQuestionResult.FailureResult("Question cannot be empty"));

        var item = new QuestionItem
        {
            Header = "提问",
            Question = question,
            Options = (options ?? []).Select(o => new QuestionOption { Label = o, Description = string.Empty }).ToList(),
            MultiSelect = multiSelect,
        };
        return ShowSingleAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
            return AskUserQuestionResult.FailureResult("No questions provided");

        if (questions.Count > 4)
            return AskUserQuestionResult.FailureResult("Maximum 4 questions allowed");

        // 校验对齐 CLI TerminalInteractiveService（空问题/选项数/重复标签）
        foreach (var q in questions)
        {
            if (string.IsNullOrWhiteSpace(q.Question))
                return AskUserQuestionResult.FailureResult("Question text cannot be empty");
            if (q.Options.Count is < 2 or > 4)
                return AskUserQuestionResult.FailureResult($"Question '{q.Question}' must have 2-4 options");
            var labels = q.Options.Select(o => o.Label).ToList();
            if (labels.Distinct().Count() != labels.Count)
                return AskUserQuestionResult.FailureResult($"Question '{q.Question}' has duplicate option labels");
        }

        var answers = new Dictionary<string, string>();
        foreach (var q in questions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ShowSingleAsync(q, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return result;
            answers[q.Question] = result.Answer
                ?? (result.SelectedOptions is { Count: > 0 } ? string.Join(", ", result.SelectedOptions) : string.Empty);
        }

        return AskUserQuestionResult.QuestionsResult(answers);
    }

    /// <summary>经 painter 在 TUI 主循环显示对话框并等待作答。</summary>
    private Task<AskUserQuestionResult> ShowSingleAsync(QuestionItem item, CancellationToken cancellationToken)
    {
        if (_painter is null || _dialogView is null)
            return Task.FromResult(AskUserQuestionResult.FailureResult("TUI 交互服务未就绪"));

        Task<AskUserQuestionResult>? dialogTask = null;
        _painter.Invoke(() => dialogTask = _dialogView.ShowAsync(item, cancellationToken));
        return dialogTask!;
    }
}
