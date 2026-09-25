namespace Core.Prompts.Testing;

/// <summary>
/// 提示词触发测试结果
/// </summary>
public sealed record PromptTriggerResult {
    /// <summary>
    /// 获取 Section 名称。
    /// </summary>
    public string SectionName { get; }

    /// <summary>
    /// 获取测试场景名称。
    /// </summary>
    public string ScenarioName { get; }

    /// <summary>
    /// 获取实际是否触发。
    /// </summary>
    public bool IsTriggered { get; }

    /// <summary>
    /// 获取预期是否触发。
    /// </summary>
    public bool ExpectedTriggered { get; }

    /// <summary>
    /// 获取触发结果是否正确（实际与预期一致）。
    /// </summary>
    public bool IsCorrect { get; }

    /// <summary>
    /// 获取触发条件描述。
    /// </summary>
    public string? ConditionDescription { get; }

    /// <summary>
    /// 获取 Section 输出内容。
    /// </summary>
    public string? Output { get; }

    /// <summary>
    /// 获取测试执行耗时。
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// 初始化 <see cref="PromptTriggerResult"/> 的新实例。
    /// </summary>
    /// <param name="sectionName">Section 名称。</param>
    /// <param name="scenarioName">测试场景名称。</param>
    /// <param name="isTriggered">实际是否触发。</param>
    /// <param name="expectedTriggered">预期是否触发。</param>
    /// <param name="isCorrect">触发结果是否正确。</param>
    /// <param name="conditionDescription">触发条件描述。</param>
    /// <param name="output">Section 输出内容。</param>
    /// <param name="duration">测试执行耗时，为 null 时使用 <see cref="TimeSpan.Zero"/>。</param>
    public PromptTriggerResult(
        string sectionName,
        string scenarioName,
        bool isTriggered,
        bool expectedTriggered,
        bool isCorrect,
        string? conditionDescription = null,
        string? output = null,
        TimeSpan? duration = null) {
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
public sealed class PromptTriggerReport {
    private readonly List<PromptTriggerResult> _results = new();

    /// <summary>
    /// 获取所有测试结果列表。
    /// </summary>
    public IReadOnlyList<PromptTriggerResult> Results => _results;

    /// <summary>
    /// 获取测试总数。
    /// </summary>
    public int TotalCount => _results.Count;

    /// <summary>
    /// 获取结果正确的测试数。
    /// </summary>
    public int GetCorrectCount() => _results.Count(r => r.IsCorrect);

    /// <summary>
    /// 获取结果错误的测试数。
    /// </summary>
    public int GetIncorrectCount() => _results.Count(r => !r.IsCorrect);

    /// <summary>
    /// 获取已触发的测试数。
    /// </summary>
    public int GetTriggeredCount() => _results.Count(r => r.IsTriggered);

    /// <summary>
    /// 获取未触发的测试数。
    /// </summary>
    public int GetNotTriggeredCount() => _results.Count(r => !r.IsTriggered);

    /// <summary>
    /// 获取所有测试的总耗时。
    /// </summary>
    public TimeSpan GetTotalDuration() => TimeSpan.FromTicks(_results.Sum(r => r.Duration.Ticks));

    /// <summary>
    /// 添加单个测试结果。
    /// </summary>
    /// <param name="result">要添加的测试结果。</param>
    public void AddResult(PromptTriggerResult result) {
        _results.Add(result);
    }

    /// <summary>
    /// 添加多个测试结果。
    /// </summary>
    /// <param name="results">要添加的测试结果集合。</param>
    public void AddResults(IEnumerable<PromptTriggerResult> results) {
        _results.AddRange(results);
    }

    /// <summary>
    /// 按场景分组的结果
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PromptTriggerResult>> GetResultsByScenario() {
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
    public IReadOnlyDictionary<string, IReadOnlyList<PromptTriggerResult>> GetResultsBySection() {
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
    public IReadOnlyList<PromptTriggerResult> GetFailedResults() {
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