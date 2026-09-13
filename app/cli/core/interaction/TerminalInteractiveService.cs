namespace JoinCode.Cli.Interaction;

/// <summary>
/// 终端交互服务 — 真正的控制台多选交互实现，替代 Core 层的 Mock InteractiveService
/// 消费方: UserInteractionToolHandlers (MCP AskUserQuestion 工具)
/// 注册: CliModule 中覆盖 Mock 注册，Order=80 在 CoreModule(Order=30) 之后
/// </summary>
public sealed class TerminalInteractiveService : IInteractiveService
{
    private readonly ILogger<TerminalInteractiveService>? _logger;

    public TerminalInteractiveService(ILogger<TerminalInteractiveService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 单问题提问 — 显示选项列表，读取用户数字选择
    /// </summary>
    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(AskUserQuestionResult.FailureResult("Question cannot be empty"));

        if (options is null || options.Count == 0)
        {
            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{question}{AnsiStyleConstants.Reset}");
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
        CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
            return Task.FromResult(AskUserQuestionResult.FailureResult("No questions provided"));

        if (questions.Count > 4)
            return Task.FromResult(AskUserQuestionResult.FailureResult("Maximum 4 questions allowed"));

        var answers = new Dictionary<string, string>();
        foreach (var q in questions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(q.Question))
                return Task.FromResult(AskUserQuestionResult.FailureResult("Question text cannot be empty"));

            if (q.Options.Count < 2 || q.Options.Count > 4)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' must have 2-4 options"));

            var labels = q.Options.Select(o => o.Label).ToList();
            if (labels.Distinct().Count() != labels.Count)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' has duplicate option labels"));

            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{q.Header}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine(q.Question);
            TerminalHelper.WriteLine();

            for (int i = 0; i < q.Options.Count; i++)
            {
                var opt = q.Options[i];
                TerminalHelper.WriteLine($"  {AnsiStyleConstants.Bold}{i + 1}.{AnsiStyleConstants.Reset} {opt.Label}");
                if (!string.IsNullOrWhiteSpace(opt.Description))
                    TerminalHelper.WriteLine($"     {AnsiStyleConstants.Dim}{opt.Description}{AnsiStyleConstants.Reset}");
            }

            TerminalHelper.WriteLine();

            List<int> selected;
            if (q.MultiSelect)
            {
                TerminalHelper.WriteRaw($"请选择 (1-{q.Options.Count}, 逗号分隔, 0=取消): ");
                selected = ReadMultiSelection(q.Options.Count, cancellationToken);
            }
            else
            {
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
    private static List<string>? DisplayQuestion(string question, List<string> options, bool multiSelect, CancellationToken ct)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{question}{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine();

        for (int i = 0; i < options.Count; i++)
        {
            TerminalHelper.WriteLine($"  {AnsiStyleConstants.Bold}{i + 1}.{AnsiStyleConstants.Reset} {options[i]}");
        }

        TerminalHelper.WriteLine();

        if (multiSelect)
        {
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
    private static List<int> ReadSingleSelection(int maxOption, CancellationToken ct)
    {
        while (true)
        {
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
    private static List<int> ReadMultiSelection(int maxOption, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var input = TerminalHelper.ReadLine().Trim();

            if (input == "0" || string.IsNullOrEmpty(input))
                return [];

            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var indices = new List<int>();
            var valid = true;

            foreach (var part in parts)
            {
                if (int.TryParse(part, out var idx) && idx >= 1 && idx <= maxOption)
                    indices.Add(idx);
                else
                {
                    valid = false;
                    break;
                }
            }

            if (valid && indices.Count > 0)
                return indices.Distinct().ToList();

            TerminalHelper.WriteRaw($"无效输入,请输入 1-{maxOption} (逗号分隔) 或 0 取消: ");
        }
    }
}
