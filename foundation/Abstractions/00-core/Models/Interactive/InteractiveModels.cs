namespace JoinCode.Abstractions.Models.Interactive;

public sealed record QuestionOption
{
    public required string Label { get; init; }
    public required string Description { get; init; }
    public string? Preview { get; init; }
}

public sealed record QuestionItem
{
    public required string Question { get; init; }
    public required string Header { get; init; }
    public required List<QuestionOption> Options { get; init; }
    public bool MultiSelect { get; init; }
}

public sealed record AskUserQuestionResult
{
    public required bool Success { get; init; }
    public Dictionary<string, string>? Answers { get; init; }
    public string? Answer { get; init; }
    public List<string>? SelectedOptions { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Cancelled { get; init; }

    public static AskUserQuestionResult SuccessResult(string answer) => new()
    {
        Success = true,
        Answer = answer
    };

    public static AskUserQuestionResult MultiSelectResult(List<string> selectedOptions) => new()
    {
        Success = true,
        SelectedOptions = selectedOptions
    };

    public static AskUserQuestionResult QuestionsResult(Dictionary<string, string> answers) => new()
    {
        Success = true,
        Answers = answers
    };

    public static AskUserQuestionResult FailureResult(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };

    public static AskUserQuestionResult CancelledResult() => new()
    {
        Success = false,
        Cancelled = true,
        ErrorMessage = "User cancelled"
    };
}
