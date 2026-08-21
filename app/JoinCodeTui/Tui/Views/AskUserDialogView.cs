namespace JoinCode.Tui.Views;

/// <summary>
/// AskUserQuestion 问答弹窗组件 — MCP ask_user_question 工具的 TUI 交互载体。
/// 对齐 CLI TerminalInteractiveService 的输入语义：数字序号选择（1-based）、0=取消、
/// 多选逗号分隔；无选项时 TextField 自由输入。经 <see cref="AskUserSelectionParser"/> 解析。
/// </summary>
public sealed class AskUserDialogView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _headerLabel;
    private readonly Label _questionLabel;
    private readonly Label _optionsLabel;
    private readonly TextField _inputField;
    private readonly Button _submitButton;
    private readonly Button _cancelButton;

    /// <summary>当前问题（渲染与提交校验共用）</summary>
    private QuestionItem? _currentQuestion;

    private TaskCompletionSource<AskUserQuestionResult>? _pendingResponse;

    public AskUserDialogView()
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Auto(),
            Visible = false,
        };

        _headerLabel = new Label
        {
            Text = string.Empty,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };

        _questionLabel = new Label
        {
            Text = string.Empty,
            X = 0,
            Y = Pos.Bottom(_headerLabel),
            Width = Dim.Fill(),
        };

        _optionsLabel = new Label
        {
            Text = string.Empty,
            X = 0,
            Y = Pos.Bottom(_questionLabel),
            Width = Dim.Fill(),
        };

        _inputField = new TextField
        {
            X = 0,
            Y = Pos.Bottom(_optionsLabel) + 1,
            Width = Dim.Fill(20),
        };

        _submitButton = new Button
        {
            Text = "确定 (Enter)",
            X = 0,
            Y = Pos.Bottom(_inputField) + 1,
        };
        _submitButton.Accepting += OnSubmit;

        _cancelButton = new Button
        {
            Text = "取消 (0/Esc)",
            X = Pos.Right(_submitButton),
            Y = Pos.Bottom(_inputField) + 1,
        };
        _cancelButton.Accepting += OnCancel;

        _container.Add(_headerLabel, _questionLabel, _optionsLabel, _inputField, _submitButton, _cancelButton);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>
    /// 显示单个问题并等待用户作答。
    /// </summary>
    /// <param name="question">问题定义（Header/Question/Options/MultiSelect）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AskUserQuestionResult> ShowAsync(QuestionItem question, CancellationToken cancellationToken = default)
    {
        _currentQuestion = question;
        _pendingResponse = new TaskCompletionSource<AskUserQuestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var optionsText = question.Options.Count == 0
            ? "（自由输入，留空取消）"
            : string.Join("\n", question.Options.Select((o, i) =>
                $"  {i + 1}. {o.Label}" + (string.IsNullOrWhiteSpace(o.Description) ? string.Empty : $"  — {o.Description}")));
        var hint = question.Options.Count == 0
            ? "请输入:"
            : question.MultiSelect ? $"请选择 (1-{question.Options.Count}, 逗号分隔, 0=取消):" : $"请选择 (1-{question.Options.Count}, 0=取消):";

        _container.Visible = true;
        _headerLabel.Text = question.Header;
        _questionLabel.Text = question.Question;
        _optionsLabel.Text = optionsText + "\n" + hint;
        _inputField.Text = string.Empty;

        cancellationToken.Register(() => _pendingResponse.TrySetResult(AskUserQuestionResult.CancelledResult()));

#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由按钮事件启动，RunContinuationsAsynchronously 避免死锁
        return _pendingResponse.Task;
#pragma warning restore VSTHRD003
    }

    /// <summary>隐藏对话框并清空状态。</summary>
    public void Hide()
    {
        _container.Visible = false;
        _currentQuestion = null;
    }

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _container.Width = Dim.Fill();
    }

    private void OnSubmit(object? sender, EventArgs e)
    {
        if (_pendingResponse is null || _currentQuestion is null)
            return;

        // 无选项 → 自由输入模式：原样文本即答案，空白视为取消
        if (_currentQuestion.Options.Count == 0)
        {
            var freeText = _inputField.Text?.Trim() ?? string.Empty;
            Complete(string.IsNullOrWhiteSpace(freeText)
                ? AskUserQuestionResult.CancelledResult()
                : AskUserQuestionResult.SuccessResult(freeText));
            return;
        }

        var parse = AskUserSelectionParser.Parse(_inputField.Text ?? string.Empty, _currentQuestion.Options.Count, _currentQuestion.MultiSelect);
        switch (parse.Status)
        {
            case AskUserSelectionStatus.Cancel:
                Complete(AskUserQuestionResult.CancelledResult());
                break;
            case AskUserSelectionStatus.Invalid:
                // 无效输入不关窗 — 提示后重新输入（对齐 CLI 重试提示语义）
                _optionsLabel.Text = _optionsLabel.Text.Split('\n')[..^1].Aggregate((a, b) => a + "\n" + b)
                    + $"\n无效输入，请输入 1-{_currentQuestion.Options.Count}{(_currentQuestion.MultiSelect ? " (逗号分隔)" : "")} 或 0 取消:";
                break;
            default:
                Complete(BuildResult(parse.Indices));
                break;
        }
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        _pendingResponse?.TrySetResult(AskUserQuestionResult.CancelledResult());
        Hide();
    }

    /// <summary>按选择序号构建结果 — 单选走 Answer，多选走 SelectedOptions</summary>
    private AskUserQuestionResult BuildResult(IReadOnlyList<int> indices)
    {
        var question = _currentQuestion!;
        var labels = indices.Select(i => question.Options[i - 1].Label).ToList();
        return question.MultiSelect
            ? AskUserQuestionResult.MultiSelectResult(labels)
            : AskUserQuestionResult.SuccessResult(labels[0]);
    }

    private void Complete(AskUserQuestionResult result)
    {
        _pendingResponse?.TrySetResult(result);
        Hide();
    }
}
