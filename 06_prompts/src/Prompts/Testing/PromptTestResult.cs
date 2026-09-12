namespace Core.Prompts.Testing;

/// <summary>
/// 提示词触发测试结果
/// </summary>
public sealed record PromptTriggerResult
{
    public string SectionName { get; }
    public string ScenarioName { get; }
    public bool IsTriggered { get; }
    public bool ExpectedTriggered { get; }
    public bool IsCorrect { get; }
    public string? ConditionDescription { get; }
    public string? Output { get; }
    public TimeSpan Duration { get; }

    public PromptTriggerResult(
        string sectionName,
        string scenarioName,
        bool isTriggered,
        bool expectedTriggered,
        bool isCorrect,
        string? conditionDescription = null,
        string? output = null,
        TimeSpan? duration = null)
    {
        SectionName = sectionName;
        ScenarioName = scenarioName;
        IsTriggered = isTriggered;
        ExpectedTriggered = expectedTriggered;
        IsCorrect = isCorrect;
        ConditionDescription = conditionDescription;
        Output = output;
        Duration = duration ?? TimeSpan.Zero;
    }
}

/// <summary>
/// 提示词触发测试报告
/// </summary>
public sealed class PromptTriggerReport
{
    private readonly List<PromptTriggerResult> _results = new();

    public IReadOnlyList<PromptTriggerResult> Results => _results;

    public int TotalCount => _results.Count;

    public int CorrectCount => _results.Count(r => r.IsCorrect);

    public int IncorrectCount => _results.Count(r => !r.IsCorrect);

    public int TriggeredCount => _results.Count(r => r.IsTriggered);

    public int NotTriggeredCount => _results.Count(r => !r.IsTriggered);

    public TimeSpan TotalDuration => TimeSpan.FromTicks(_results.Sum(r => r.Duration.Ticks));

    public void AddResult(PromptTriggerResult result)
    {
        _results.Add(result);
    }

    public void AddResults(IEnumerable<PromptTriggerResult> results)
    {
        _results.AddRange(results);
    }

    /// <summary>
    /// 按场景分组的结果
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PromptTriggerResult>> GetResultsByScenario()
    {
        return _results
            .GroupBy(r => r.ScenarioName)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PromptTriggerResult>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按Section分组的结果
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PromptTriggerResult>> GetResultsBySection()
    {
        return _results
            .GroupBy(r => r.SectionName)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PromptTriggerResult>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取失败的测试结果
    /// </summary>
    public IReadOnlyList<PromptTriggerResult> GetFailedResults()
    {
        return _results.Where(r => !r.IsCorrect).ToList();
    }
}

/// <summary>
/// 测试场景定义
/// </summary>
public sealed record TestScenario(
    string Name,
    PromptTestConfig Config
);
