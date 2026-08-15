using Avalonia.Controls;
using Avalonia.Threading;
using JoinCode.Abstractions.Models.Interactive;
using JoinCode.Gui.Views;

namespace JoinCode.Gui.Hosting;

/// <summary>
/// Avalonia GUI 交互服务 — 通过弹窗实现 AskUserQuestion 的多选交互。
/// 替代 Core 层的 Mock InteractiveService（自动选第一项）。
/// 注册: GuiInteractionModule (Order=80) 覆盖 CoreModule 的 Mock 注册。
/// 线程安全: 工具管道在后台线程调用，弹窗通过 Dispatcher.UIThread 调度到 UI 线程。
/// </summary>
public sealed class AvaloniaInteractiveService : IInteractiveService
{
    private readonly ILogger<AvaloniaInteractiveService>? _logger;

    /// <summary>
    /// 弹窗回调 — 由 MainWindow 在初始化时设置，提供父窗口引用。
    /// 回调在 UI 线程上执行，返回用户选择结果。
    /// </summary>
    public Func<QuestionItem, Task<AskUserQuestionResult>>? ShowDialogCallback { get; set; }

    public AvaloniaInteractiveService(ILogger<AvaloniaInteractiveService>? logger = null)
    {
        _logger = logger;
    }

    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default)
    {
        var questionItem = new QuestionItem
        {
            Question = question,
            Header = "确认",
            Options = options?.Select(o => new QuestionOption { Label = o, Description = "" }).ToList()
                ?? [new() { Label = "确定", Description = "" }, new() { Label = "取消", Description = "" }],
            MultiSelect = multiSelect
        };

        return AskUserQuestionsAsync([questionItem], cancellationToken);
    }

    public async Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
            return AskUserQuestionResult.FailureResult("No questions provided");

        if (questions.Count > 4)
            return AskUserQuestionResult.FailureResult("Maximum 4 questions allowed");

        var answers = new Dictionary<string, string>();

        foreach (var q in questions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShowDialogCallback is null)
            {
                _logger?.LogWarning("[AvaloniaInteractive] ShowDialogCallback 未设置，回退到自动选择第一项");
                answers[q.Question] = q.Options[0].Label;
                continue;
            }

            AskUserQuestionResult result;
            if (Dispatcher.UIThread.CheckAccess())
            {
                result = await ShowDialogCallback(q).ConfigureAwait(false);
            }
            else
            {
                var tcs = new TaskCompletionSource<AskUserQuestionResult>();
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        var r = await ShowDialogCallback(q).ConfigureAwait(false);
                        tcs.SetResult(r);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, DispatcherPriority.Normal);
                result = await tcs.Task.ConfigureAwait(false);
            }

            if (!result.Success || result.Cancelled)
                return AskUserQuestionResult.CancelledResult();

            answers[q.Question] = result.Answer
                ?? (result.SelectedOptions is not null ? string.Join(", ", result.SelectedOptions) : "")
                ?? q.Options[0].Label;
        }

        return AskUserQuestionResult.QuestionsResult(answers);
    }
}
