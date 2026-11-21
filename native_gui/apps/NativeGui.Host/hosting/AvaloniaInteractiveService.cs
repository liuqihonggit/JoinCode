namespace JoinCode.Gui.Hosting;

/// <summary>
/// Avalonia GUI 交互服务 — 通过弹窗实现 AskUserQuestion 的多选交互。
/// 替代 Core 层的 Mock InteractiveService（自动选第一项）。
/// 注册: GuiInteractionModule (Order=80) 覆盖 CoreModule 的 Mock 注册。
/// 线程安全: 工具管道在后台线程调用，弹窗通过 Dispatcher.UIThread 调度到 UI 线程。
/// </summary>
public sealed class AvaloniaInteractiveService : IInteractiveService {
    private readonly ILogger<AvaloniaInteractiveService>? _logger;
    private readonly IAgentNotificationQueue? _notificationQueue;

    /// <summary>后台通知轮询间隔 — 问答卡片挂起期间检测待处理通知。</summary>
    private const int BackgroundNotificationPollMs = 100;

    /// <summary>
    /// 弹窗回调 — 由 MainWindow 在初始化时设置，提供父窗口引用。
    /// 回调在 UI 线程上执行，返回用户选择结果。
    /// </summary>
    public Func<QuestionItem, Task<AskUserQuestionResult>>? ShowDialogCallback { get; set; }

    /// <summary>
    /// 批量提问回调 — DSH Web UI 模式注入：一次把全部问题交给前端卡片渲染，
    /// 避免逐题弹原生对话框（最多 4 次）打断会话。设置后优先于 <see cref="ShowDialogCallback"/>。
    /// </summary>
    public Func<List<QuestionItem>, CancellationToken, Task<AskUserQuestionResult>>? ShowQuestionsCallback { get; set; }

    /// <summary>初始化 AvaloniaInteractiveService 实例</summary>
    /// <param name="logger">日志记录器，可选</param>
    /// <param name="notificationQueue">后台通知队列，可选；存在时问答卡片挂起期间收到回调即关闭卡片返回 Deferred</param>
    public AvaloniaInteractiveService(ILogger<AvaloniaInteractiveService>? logger = null, IAgentNotificationQueue? notificationQueue = null) {
        _logger = logger;
        _notificationQueue = notificationQueue;
    }

    /// <summary>异步询问用户单个问题（通过弹窗呈现选项并等待用户选择）</summary>
    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default) {
        var questionItem = new QuestionItem {
            Question = question,
            Header = "确认",
            Options = options?.Select(o => new QuestionOption { Label = o, Description = "" }).ToList()
                ?? [new() { Label = "确定", Description = "" }, new() { Label = "取消", Description = "" }],
            MultiSelect = multiSelect
        };

        return AskUserQuestionsAsync([questionItem], cancellationToken);
    }

    /// <summary>异步询问用户多个问题（逐个弹窗呈现，最多 4 个问题）</summary>
    public async Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default) {
        if (questions.Count == 0)
            return AskUserQuestionResult.FailureResult("No questions provided");

        if (questions.Count > 4)
            return AskUserQuestionResult.FailureResult("Maximum 4 questions allowed");

        // DSH Web UI：一张卡片承载全部问题（前端答案批次经 $events 回帖）
        if (ShowQuestionsCallback is { } askBatch) {
            return await AskWithBackgroundWakeAsync(askBatch, questions, cancellationToken);
        }

        var answers = new Dictionary<string, string>();

        foreach (var q in questions) {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShowDialogCallback is null) {
                _logger?.LogWarning("[AvaloniaInteractive] ShowDialogCallback 未设置，回退到自动选择第一项");
                answers[q.Question] = q.Options[0].Label;
                continue;
            }

            AskUserQuestionResult result;
            if (Dispatcher.UIThread.CheckAccess()) {
                result = await ShowDialogCallback(q);
            } else {
                var tcs = new TaskCompletionSource<AskUserQuestionResult>();
                Dispatcher.UIThread.Post(async () => {
                    try {
                        var r = await ShowDialogCallback(q);
                        tcs.SetResult(r);
                    } catch (Exception ex) {
                        tcs.SetException(ex);
                    }
                }, DispatcherPriority.Normal);
                result = await tcs.Task;
            }

            if (!result.Success || result.Cancelled)
                return AskUserQuestionResult.CancelledResult();

            answers[q.Question] = result.Answer
                ?? (result.SelectedOptions is not null ? string.Join(", ", result.SelectedOptions) : "")
                ?? q.Options[0].Label;
        }

        return AskUserQuestionResult.QuestionsResult(answers);
    }

    /// <summary>问答卡片挂起期间检测后台通知 — 回调到达即取消卡片并返回 Deferred，让循环先处理回调。</summary>
    private async Task<AskUserQuestionResult> AskWithBackgroundWakeAsync(
        Func<List<QuestionItem>, CancellationToken, Task<AskUserQuestionResult>> askBatch,
        List<QuestionItem> questions,
        CancellationToken cancellationToken) {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var askTask = askBatch(questions, linkedCts.Token);

        while (!askTask.IsCompleted) {
            if (_notificationQueue?.HasPendingNotifications == true) {
                await linkedCts.CancelAsync();
                await AwaitAskQuietlyAsync(askTask);
                _logger?.LogInformation("[AvaloniaInteractive] Background notification arrived, closing pending question card");
                return AskUserQuestionResult.DeferredResult();
            }

            await Task.WhenAny(askTask, Task.Delay(BackgroundNotificationPollMs, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await askTask;
    }

    /// <summary>等待被取消的问答任务收尾 — 仅吞取消异常，其他异常不掩盖。</summary>
    private async Task AwaitAskQuietlyAsync(Task<AskUserQuestionResult> askTask) {
        try {
            await askTask;
        } catch (OperationCanceledException) {
            _logger?.LogDebug("[AvaloniaInteractive] Pending question card cancelled by background wake");
        }
    }
}