namespace NativeTui.Host;

/// <summary>
/// TUI 交互服务 — IInteractiveService 的原生 TUI 实现（替代 Core.Planning.InteractiveService 自动首选项占位）：
/// ask_user_question 工具调用 → TuiAskBridge 挂起问答对话框（单选/多选/多问题，按键作答）→
/// 映射回引擎 AskUserQuestionResult。后台回调到达时对话框返回 Deferred，Tool handler 交给 Agent 先处理回调再重问。
/// 注册：TuiInteractionModule（Order=80，覆盖 CoreModule 的自动应答实现）。
/// </summary>
public sealed class TuiInteractiveService(TuiAskBridge bridge) : IInteractiveService {
    /// <summary>
    /// 单问题提问 — 无选项或选项不足 2 项时返回 Failure 诱导（TUI 对话框需要可选项）；
    /// 多选返回选中标签列表，单选返回单个标签。
    /// </summary>
    public Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(AskUserQuestionResult.FailureResult("Question cannot be empty"));

        if (options is null || options.Count < 2)
            return Task.FromResult(AskUserQuestionResult.FailureResult(
                "TUI 问答至少需要 2 个选项；请为问题提供 2-4 个可选项后重新调用。"));

        var item = new TuiAskQuestion(
            Question: question,
            Header: "问题",
            Options: options.Select(label => new TuiAskOption(label, string.Empty)).ToList(),
            MultiSelect: multiSelect);
        var result = bridge.Ask([item], cancellationToken);

        if (result.Succeeded && result.Answers.TryGetValue(question, out var answer)) {
            var labels = SplitAnswer(answer);
            return Task.FromResult(multiSelect
                ? AskUserQuestionResult.MultiSelectResult(labels)
                : AskUserQuestionResult.SuccessResult(labels.Count > 0 ? labels[0] : answer));
        }

        return Task.FromResult(MapNonSuccess(result));
    }

    /// <summary>
    /// 多问题批量提问 — 每个问题逐题挂起作答，最后一题提交后整体返回答案字典。
    /// 空问题集直接 Failure 诱导。
    /// </summary>
    public Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default) {
        if (questions.Count == 0)
            return Task.FromResult(AskUserQuestionResult.FailureResult("No questions provided"));

        var items = questions.Select(q => new TuiAskQuestion(
            Question: q.Question,
            Header: q.Header,
            Options: q.Options.Select(o => new TuiAskOption(o.Label, o.Description)).ToList(),
            MultiSelect: q.MultiSelect)).ToList();
        var result = bridge.Ask(items, cancellationToken);

        if (result.Succeeded)
            return Task.FromResult(AskUserQuestionResult.QuestionsResult(new Dictionary<string, string>(result.Answers)));

        return Task.FromResult(MapNonSuccess(result));
    }

    private static AskUserQuestionResult MapNonSuccess(TuiAskResult result) {
        if (result.Cancelled)
            return AskUserQuestionResult.CancelledResult();

        if (result.Deferred)
            return AskUserQuestionResult.DeferredResult(
                result.Answers.Count > 0 ? new Dictionary<string, string>(result.Answers) : null);

        return AskUserQuestionResult.FailureResult(result.Error ?? "问答失败（未知原因）；请重新提问。");
    }

    private static List<string> SplitAnswer(string answer) =>
        answer.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
