namespace JoinCode.Abstractions.Models.Interactive;

public sealed record QuestionOption {
    /// <summary>获取选项标签。</summary>
    public required string Label { get; init; }
    /// <summary>获取选项描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取选项预览内容。</summary>
    public string? Preview { get; init; }
}

public sealed record QuestionItem {
    /// <summary>获取问题文本。</summary>
    public required string Question { get; init; }
    /// <summary>获取问题标题。</summary>
    public required string Header { get; init; }
    /// <summary>获取选项列表。</summary>
    public required List<QuestionOption> Options { get; init; }
    /// <summary>获取是否允许多选。</summary>
    public bool MultiSelect { get; init; }
}

public sealed record AskUserQuestionResult {
    /// <summary>获取是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取问题答案字典。</summary>
    public Dictionary<string, string>? Answers { get; init; }
    /// <summary>获取单个答案。</summary>
    public string? Answer { get; init; }
    /// <summary>获取已选选项列表。</summary>
    public List<string>? SelectedOptions { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取是否已取消。</summary>
    public bool Cancelled { get; init; }

    /// <summary>创建成功结果。</summary>
    public static AskUserQuestionResult SuccessResult(string answer) => new() {
        Success = true,
        Answer = answer
    };

    /// <summary>创建多选结果。</summary>
    public static AskUserQuestionResult MultiSelectResult(List<string> selectedOptions) => new() {
        Success = true,
        SelectedOptions = selectedOptions
    };

    /// <summary>创建多问题结果。</summary>
    public static AskUserQuestionResult QuestionsResult(Dictionary<string, string> answers) => new() {
        Success = true,
        Answers = answers
    };

    /// <summary>创建失败结果。</summary>
    public static AskUserQuestionResult FailureResult(string errorMessage) => new() {
        Success = false,
        ErrorMessage = errorMessage
    };

    /// <summary>创建取消结果。</summary>
    public static AskUserQuestionResult CancelledResult() => new() {
        Success = false,
        Cancelled = true,
        ErrorMessage = "User cancelled"
    };
}