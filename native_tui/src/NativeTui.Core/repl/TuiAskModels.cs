namespace NativeTui.Core.Repl;

/// <summary>TUI 问答选项 — 标签 + 描述（NativeTui.Core 零依赖 DTO，宿主负责与引擎 QuestionItem 映射）。</summary>
public sealed record TuiAskOption(string Label, string Description);

/// <summary>TUI 问答问题 — 问题文本 + 标题 + 选项 + 是否多选。</summary>
public sealed record TuiAskQuestion(string Question, string Header, IReadOnlyList<TuiAskOption> Options, bool MultiSelect);

/// <summary>TUI 问答结果 — 成功（Answers 有值）/ 用户取消 / 后台回调提前关闭 / 失败（Error 有值）。</summary>
public sealed record TuiAskResult(bool Succeeded, bool Cancelled, bool Deferred, IReadOnlyDictionary<string, string> Answers)
{
    /// <summary>失败原因（仅失败结果携带）。</summary>
    public string? Error { get; init; }

    /// <summary>成功结果。</summary>
    public static TuiAskResult Success(IReadOnlyDictionary<string, string> answers) => new(true, false, false, answers);

    /// <summary>用户取消。</summary>
    public static TuiAskResult CancelledResult { get; } = new(false, true, false, new Dictionary<string, string>());

    /// <summary>后台回调到达提前关闭（携带已答部分）。</summary>
    public static TuiAskResult DeferredResult(IReadOnlyDictionary<string, string>? partial) =>
        new(false, false, true, partial ?? new Dictionary<string, string>());

    /// <summary>失败结果 — 宿主未就绪等不可交互场景。</summary>
    public static TuiAskResult Failure(string error) =>
        new(false, false, false, new Dictionary<string, string>()) { Error = error };
}
