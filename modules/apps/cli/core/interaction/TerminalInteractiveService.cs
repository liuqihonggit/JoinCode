namespace JoinCode.Cli.Interaction;

/// <summary>
/// 终端交互服务 — 真正的控制台多选交互实现，替代 Core 层的 Mock InteractiveService
/// 消费方: UserInteractionToolHandlers (MCP AskUserQuestion 工具)
/// 注册: CliModule 中覆盖 Mock 注册，Order=80 在 CoreModule(Order=30) 之后
/// </summary>
public sealed class TerminalInteractiveService : IInteractiveService {
    private readonly ILogger<TerminalInteractiveService>? _logger;

    /// <summary>
    /// 构造函数 — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">日志记录器，可选</param>
    public TerminalInteractiveService(ILogger<TerminalInteractiveService>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 单问题提问 — 显示选项列表，读取用户数字选择
    /// </summary>
    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(AskUserQuestionResult.FailureResult("Question cannot be empty"));

        if (TerminalHelper.IsHeadless)
            return Task.FromResult(AskUserQuestionResult.FailureResult("Non-interactive mode: cannot prompt for user input"));

        if (options is null || options.Count == 0) {
            TerminalHelper.WriteLine();
            WriteWrapped(Sanitize(question), bold: true, indent: 0);
            TerminalHelper.WriteRaw("> ");
            var freeInput = TerminalHelper.ReadLine();
            return Task.FromResult(string.IsNullOrWhiteSpace(freeInput)
                ? AskUserQuestionResult.CancelledResult()
                : AskUserQuestionResult.SuccessResult(freeInput));
        }

        var selection = DisplayQuestion(question, options, multiSelect, cancellationToken);
        if (selection is null)
            return Task.FromResult(AskUserQuestionResult.CancelledResult());

        if (multiSelect)
            return Task.FromResult(AskUserQuestionResult.MultiSelectResult(selection));

        return Task.FromResult(AskUserQuestionResult.SuccessResult(selection[0]));
    }

    /// <summary>
    /// 多问题批量提问 — 逐个显示，收集所有答案
    /// </summary>
    public Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default) {
        if (questions.Count == 0)
            return Task.FromResult(AskUserQuestionResult.FailureResult("No questions provided"));

        if (questions.Count > 4)
            return Task.FromResult(AskUserQuestionResult.FailureResult("Maximum 4 questions allowed"));

        var answers = new Dictionary<string, string>();
        foreach (var q in questions) {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(q.Question))
                return Task.FromResult(AskUserQuestionResult.FailureResult("Question text cannot be empty"));

            if (q.Options.Count < 2 || q.Options.Count > 4)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' must have 2-4 options"));

            var labels = q.Options.Select(o => o.Label).ToList();
            if (labels.Distinct().Count() != labels.Count)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' has duplicate option labels"));

            if (TerminalHelper.IsHeadless)
                return Task.FromResult(AskUserQuestionResult.FailureResult("Non-interactive mode: cannot prompt for user input"));

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

            List<int> selected;
            if (q.MultiSelect) {
                TerminalHelper.WriteRaw($"请选择 (1-{q.Options.Count}, 逗号分隔, 0=取消): ");
                selected = ReadMultiSelection(q.Options.Count, cancellationToken);
            } else {
                TerminalHelper.WriteRaw($"请选择 (1-{q.Options.Count}, 0=取消): ");
                selected = ReadSingleSelection(q.Options.Count, cancellationToken);
            }

            if (selected.Count == 0)
                return Task.FromResult(AskUserQuestionResult.CancelledResult());

            answers[q.Question] = string.Join(", ", selected.Select(idx => q.Options[idx - 1].Label));
            _logger?.LogInformation("[TerminalInteractive] Q: {Question} A: {Answer}", q.Question, answers[q.Question]);
        }

        return Task.FromResult(AskUserQuestionResult.QuestionsResult(answers));
    }

    /// <summary>
    /// 显示单问题并获取选择索引列表
    /// </summary>
    private static List<string>? DisplayQuestion(string question, List<string> options, bool multiSelect, CancellationToken ct) {
        TerminalHelper.WriteLine();
        WriteWrapped(Sanitize(question), bold: true, indent: 0);
        TerminalHelper.WriteLine();

        for (var i = 0; i < options.Count; i++) {
            WriteOptionLabel(i + 1, options[i]);
        }

        TerminalHelper.WriteLine();

        if (multiSelect) {
            TerminalHelper.WriteRaw($"请选择 (1-{options.Count}, 逗号分隔, 0=取消): ");
            var indices = ReadMultiSelection(options.Count, ct);
            return indices.Select(idx => options[idx - 1]).ToList();
        }

        TerminalHelper.WriteRaw($"请选择 (1-{options.Count}, 0=取消): ");
        var single = ReadSingleSelection(options.Count, ct);
        return single.Count == 0 ? null : [options[single[0] - 1]];
    }

    /// <summary>
    /// 读取单选输入，返回选中索引(1-based)，空列表表示取消
    /// </summary>
    private static List<int> ReadSingleSelection(int maxOption, CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();
            var input = TerminalHelper.ReadLine().Trim();

            if (input == "0" || string.IsNullOrEmpty(input))
                return [];

            if (int.TryParse(input, out var idx) && idx >= 1 && idx <= maxOption)
                return [idx];

            TerminalHelper.WriteRaw($"无效输入,请输入 1-{maxOption} 或 0 取消: ");
        }
    }

    /// <summary>
    /// 读取多选输入(逗号分隔)，返回选中索引列表(1-based)，空列表表示取消
    /// </summary>
    private static List<int> ReadMultiSelection(int maxOption, CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();
            var input = TerminalHelper.ReadLine().Trim();

            if (input == "0" || string.IsNullOrEmpty(input))
                return [];

            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var indices = new List<int>();
            var valid = true;

            foreach (var part in parts) {
                if (int.TryParse(part, out var idx) && idx >= 1 && idx <= maxOption)
                    indices.Add(idx);
                else {
                    valid = false;
                    break;
                }
            }

            if (valid && indices.Count > 0)
                return indices.Distinct().ToList();

            TerminalHelper.WriteRaw($"无效输入,请输入 1-{maxOption} (逗号分隔) 或 0 取消: ");
        }
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
}