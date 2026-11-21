namespace JoinCode.Cli.Interaction;

/// <summary>
/// 终端交互服务 — 真正的控制台多选交互实现，替代 Core 层的 Mock InteractiveService
/// 消费方: UserInteractionToolHandlers (MCP AskUserQuestion 工具)
/// 注册: CliModule 中覆盖 Mock 注册，Order=80 在 CoreModule(Order=30) 之后
/// 等待输入期间若后台任务回调到达，立即关闭本次提问返回 Deferred，让 Agent 循环先处理回调
/// </summary>
public sealed class TerminalInteractiveService : IInteractiveService {
    private readonly ILogger<TerminalInteractiveService>? _logger;
    private readonly IAgentNotificationQueue? _notificationQueue;
    private readonly InteractiveInputWaiter _inputWaiter;

    /// <summary>
    /// 构造函数 — 注入可选日志记录器与后台通知队列
    /// </summary>
    /// <param name="logger">日志记录器，可选</param>
    /// <param name="notificationQueue">后台代理/任务完成通知队列，可选；存在时用于唤醒阻塞中的提问</param>
    public TerminalInteractiveService(ILogger<TerminalInteractiveService>? logger = null, IAgentNotificationQueue? notificationQueue = null) {
        _logger = logger;
        _notificationQueue = notificationQueue;
        _inputWaiter = new InteractiveInputWaiter(
            IsInputReady,
            HasPendingBackgroundNotification,
            ct => Task.Delay(100, ct));
    }

    /// <summary>非阻塞检查输入是否就绪；无控制台句柄（如 ForceInteractive + 重定向）时退化为立即读取。</summary>
    private static bool IsInputReady() {
        try {
            return TerminalHelper.KeyAvailable;
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return true;
        }
    }

    /// <summary>是否收到后台任务回调 — 有待处理通知即视为需要立即交还控制权。</summary>
    private bool HasPendingBackgroundNotification() => _notificationQueue?.HasPendingNotifications == true;

    /// <summary>
    /// 单问题提问 — 显示选项列表，读取用户数字选择
    /// </summary>
    public async Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(question))
            return AskUserQuestionResult.FailureResult("Question cannot be empty");

        if (TerminalHelper.IsHeadless)
            return AskUserQuestionResult.FailureResult("Non-interactive mode: cannot prompt for user input");

        if (options is null || options.Count == 0) {
            TerminalHelper.WriteLine();
            WriteWrapped(Sanitize(question), bold: true, indent: 0);
            TerminalHelper.WriteRaw("> ");
            if (!await WaitForInputOrDeferAsync(cancellationToken).ConfigureAwait(false))
                return AskUserQuestionResult.DeferredResult();
            var freeInput = TerminalHelper.ReadLine();
            return string.IsNullOrWhiteSpace(freeInput)
                ? AskUserQuestionResult.CancelledResult()
                : AskUserQuestionResult.SuccessResult(freeInput);
        }

        var selection = await DisplayQuestionAsync(question, options, multiSelect, cancellationToken).ConfigureAwait(false);
        if (selection.Deferred)
            return AskUserQuestionResult.DeferredResult();
        if (selection.Indices.Count == 0)
            return AskUserQuestionResult.CancelledResult();

        var labels = selection.Indices.Select(idx => options[idx - 1]).ToList();
        return multiSelect
            ? AskUserQuestionResult.MultiSelectResult(labels)
            : AskUserQuestionResult.SuccessResult(labels[0]);
    }

    /// <summary>
    /// 多问题批量提问 — 逐个显示，收集所有答案
    /// </summary>
    public async Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default) {
        if (questions.Count == 0)
            return AskUserQuestionResult.FailureResult("No questions provided");

        if (questions.Count > 4)
            return AskUserQuestionResult.FailureResult("Maximum 4 questions allowed");

        var answers = new Dictionary<string, string>();
        foreach (var q in questions) {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(q.Question))
                return AskUserQuestionResult.FailureResult("Question text cannot be empty");

            if (q.Options.Count < 2 || q.Options.Count > 4)
                return AskUserQuestionResult.FailureResult($"Question '{q.Question}' must have 2-4 options");

            var labels = q.Options.Select(o => o.Label).ToList();
            if (labels.Distinct().Count() != labels.Count)
                return AskUserQuestionResult.FailureResult($"Question '{q.Question}' has duplicate option labels");

            if (TerminalHelper.IsHeadless)
                return AskUserQuestionResult.FailureResult("Non-interactive mode: cannot prompt for user input");

            TerminalHelper.WriteLine();
            WriteWrapped(Sanitize(q.Header), bold: true, indent: 0);
            WriteWrapped(Sanitize(q.Question), bold: false, indent: 0);
            TerminalHelper.WriteLine();

            for (var i = 0; i < q.Options.Count; i++) {
                var opt = q.Options[i];
                WriteOptionLabel(i + 1, opt.Label);
                if (!string.IsNullOrWhiteSpace(opt.Description))
                    WriteOptionDescription(opt.Description);
            }

            TerminalHelper.WriteLine();

            if (q.MultiSelect) {
                TerminalHelper.WriteRaw($"请选择 (1-{q.Options.Count}, 逗号分隔, 0=取消): ");
                var multi = await ReadSelectionAsync(q.Options.Count, multiSelect: true, cancellationToken).ConfigureAwait(false);
                if (multi.Deferred)
                    return AskUserQuestionResult.DeferredResult(answers.Count > 0 ? answers : null);
                if (multi.Indices.Count == 0)
                    return AskUserQuestionResult.CancelledResult();
                answers[q.Question] = string.Join(", ", multi.Indices.Select(idx => q.Options[idx - 1].Label));
            } else {
                TerminalHelper.WriteRaw($"请选择 (1-{q.Options.Count}, 0=取消): ");
                var single = await ReadSelectionAsync(q.Options.Count, multiSelect: false, cancellationToken).ConfigureAwait(false);
                if (single.Deferred)
                    return AskUserQuestionResult.DeferredResult(answers.Count > 0 ? answers : null);
                if (single.Indices.Count == 0)
                    return AskUserQuestionResult.CancelledResult();
                answers[q.Question] = q.Options[single.Indices[0] - 1].Label;
            }

            _logger?.LogInformation("[TerminalInteractive] Q: {Question} A: {Answer}", q.Question, answers[q.Question]);
        }

        return AskUserQuestionResult.QuestionsResult(answers);
    }

    /// <summary>
    /// 显示单问题并获取选择结果
    /// </summary>
    private async Task<SelectionReadResult> DisplayQuestionAsync(string question, List<string> options, bool multiSelect, CancellationToken ct) {
        TerminalHelper.WriteLine();
        WriteWrapped(Sanitize(question), bold: true, indent: 0);
        TerminalHelper.WriteLine();

        for (var i = 0; i < options.Count; i++) {
            WriteOptionLabel(i + 1, options[i]);
        }

        TerminalHelper.WriteLine();

        if (multiSelect) {
            TerminalHelper.WriteRaw($"请选择 (1-{options.Count}, 逗号分隔, 0=取消): ");
            return await ReadSelectionAsync(options.Count, multiSelect: true, ct).ConfigureAwait(false);
        }

        TerminalHelper.WriteRaw($"请选择 (1-{options.Count}, 0=取消): ");
        return await ReadSelectionAsync(options.Count, multiSelect: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待输入就绪；若后台任务回调到达则提前交还控制权
    /// </summary>
    private async Task<bool> WaitForInputOrDeferAsync(CancellationToken ct) {
        if (TerminalHelper.IsHeadless)
            return true;

        var outcome = await _inputWaiter.WaitAsync(ct).ConfigureAwait(false);
        if (outcome == InputWaitOutcome.InputReady)
            return true;

        TerminalHelper.WriteLine();
        TerminalHelper.WriteError("[ask] 检测到后台任务回调，先关闭本次提问返回控制权；稍后可重新提问（本次未作答不会假定任何选择）。");
        _logger?.LogInformation("[TerminalInteractive] Background notification arrived, closing pending ask");
        return false;
    }

    /// <summary>
    /// 读取选择输入，返回选中索引(1-based)或延迟/取消标记
    /// </summary>
    private async Task<SelectionReadResult> ReadSelectionAsync(int maxOption, bool multiSelect, CancellationToken ct) {
        while (true) {
            if (!await WaitForInputOrDeferAsync(ct).ConfigureAwait(false))
                return SelectionReadResult.DeferredResult;

            var input = TerminalHelper.ReadLine().Trim();

            if (input == "0" || string.IsNullOrEmpty(input))
                return SelectionReadResult.CancelledResult;

            var indices = multiSelect
                ? ParseMultiSelection(input, maxOption)
                : ParseSingleSelection(input, maxOption);

            if (indices is not null)
                return new SelectionReadResult(indices, Deferred: false);

            var hint = multiSelect
                ? $"无效输入,请输入 1-{maxOption} (逗号分隔) 或 0 取消: "
                : $"无效输入,请输入 1-{maxOption} 或 0 取消: ";
            TerminalHelper.WriteRaw(hint);
        }
    }

    /// <summary>
    /// 解析单选输入，无效返回 null
    /// </summary>
    private static List<int>? ParseSingleSelection(string input, int maxOption) =>
        int.TryParse(input, out var idx) && idx >= 1 && idx <= maxOption ? [idx] : null;

    /// <summary>
    /// 解析多选输入(逗号分隔)，无效返回 null
    /// </summary>
    private static List<int>? ParseMultiSelection(string input, int maxOption) {
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var indices = new List<int>();

        foreach (var part in parts) {
            if (int.TryParse(part, out var idx) && idx >= 1 && idx <= maxOption)
                indices.Add(idx);
            else
                return null;
        }

        return indices.Count > 0 ? indices.Distinct().ToList() : null;
    }

    /// <summary>清洗 — AI 生成文本行尾归一（CR 段折叠）+ 控制字符剥除（见 <see cref="QuestionTextSanitizer.Sanitize"/>）。</summary>
    private static string Sanitize(string text) => QuestionTextSanitizer.Sanitize(text);

    /// <summary>按终端宽度折行输出 — 首行 bold 全宽、续行 dim 悬挂缩进 2 列（ANSI 样式按行独立包夹，折行后转义序列不被终端在任意列截断）。</summary>
    private static void WriteWrapped(string text, bool bold, int indent) {
        var width = Math.Max(20, TerminalHelper.GetWidth());
        foreach (var line in QuestionTextSanitizer.Wrap(text, width, indent: 2)) {
            var style = bold ? AnsiStyleEnumConstants.Bold : AnsiStyleEnumConstants.Reset;
            TerminalHelper.WriteLine($"{style}{line}{AnsiStyleEnumConstants.Reset}");
        }
    }

    /// <summary>选项标签输出 — `  N. label` 形态，超宽折行悬挂对齐标签内容列（可用宽 = 终端宽 - 缩进）。</summary>
    private static void WriteOptionLabel(int number, string label) {
        var width = Math.Max(20, TerminalHelper.GetWidth());
        var lines = QuestionTextSanitizer.Wrap(Sanitize(label), width, indent: OptionIndent);
        for (var i = 0; i < lines.Count; i++) {
            var prefix = i == 0
                ? $"  {AnsiStyleEnumConstants.Bold}{number}.{AnsiStyleEnumConstants.Reset} "
                : new string(' ', OptionIndent);
            TerminalHelper.WriteLine(prefix + lines[i]);
        }
    }

    /// <summary>选项描述输出 — 5 列缩进 dim，超宽折行悬挂对齐。</summary>
    private static void WriteOptionDescription(string description) {
        var width = Math.Max(20, TerminalHelper.GetWidth());
        foreach (var line in QuestionTextSanitizer.Wrap(Sanitize(description), width, indent: DescriptionIndent)) {
            TerminalHelper.WriteLine($"{new string(' ', DescriptionIndent)}{AnsiStyleEnumConstants.Dim}{line}{AnsiStyleEnumConstants.Reset}");
        }
    }

    /// <summary>选项行缩进列（`  N. ` = 5 列 — 续行对齐标签内容列）。</summary>
    private const int OptionIndent = 5;

    /// <summary>描述行缩进列。</summary>
    private const int DescriptionIndent = 5;

    /// <summary>选择读取结果 — 选中索引 / 取消 / 后台回调提前关闭。</summary>
    private sealed record SelectionReadResult(List<int> Indices, bool Deferred) {
        /// <summary>用户取消选择（输入 0 或空）。</summary>
        public static SelectionReadResult CancelledResult { get; } = new([], Deferred: false);
        /// <summary>后台回调到达，提前关闭提问并交还控制权。</summary>
        public static SelectionReadResult DeferredResult { get; } = new([], Deferred: true);
    }
}
