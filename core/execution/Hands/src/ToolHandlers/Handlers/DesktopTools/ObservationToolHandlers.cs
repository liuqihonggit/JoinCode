namespace Tools.Handlers;

/// <summary>
/// 观察学习工具处理器 — 演示录制/操作抽象/步骤优化（PRD L-01/L-02/L-04）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class ObservationToolHandlers
{
    private readonly IMacroRecorder _recorder;
    private readonly IObservationLearner _learner;
    private readonly ILogger<ObservationToolHandlers>? _logger;

    public ObservationToolHandlers(
        IMacroRecorder recorder,
        IObservationLearner learner,
        ILogger<ObservationToolHandlers>? logger = null)
    {
        _recorder = recorder;
        _learner = learner;
        _logger = logger;
    }

    /// <summary>开始观察学习（L-01）— 录制用户演示操作</summary>
    [McpTool("start_observation", "开始观察用户演示操作,录制鼠标键盘事件序列", "desktop")]
    public Task<ToolResult> StartObservationAsync(
        [McpToolParameter("观察会话名称", Required = true)] string sessionName,
        CancellationToken ct = default)
    {
        _recorder.StartRecording(sessionName);
        return Task.FromResult(ToolResultBuilder.Success()
            .WithText($"开始观察会话「{sessionName}」,请演示操作,完成后调用 learn_from_observation")
            .Build());
    }

    /// <summary>从观察中学习（L-02）— 停止录制并用 LLM 抽象操作模式</summary>
    [McpTool("learn_from_observation", "停止观察并用AI抽象出参数化操作逻辑", "desktop")]
    public async Task<ToolResult> LearnFromObservationAsync(CancellationToken ct = default)
    {
        if (!_recorder.IsRecording)
            return ToolResultBuilder.Error().WithText("当前未在观察状态").Build();

        var macro = _recorder.StopRecording();
        if (macro.Operations.Count == 0)
            return ToolResultBuilder.Error().WithText("观察期间未记录到任何操作").Build();

        var session = new ObservedSession(macro.Name, macro.Operations, [], macro.CreatedAt, DateTimeOffset.UtcNow);
        var logic = await _learner.AbstractAsync(session, ct).ConfigureAwait(false);

        var sb = new StringBuilder(256);
        sb.AppendLine($"从观察中学习到操作模式:");
        sb.AppendLine($"  名称: {logic.Name}");
        sb.AppendLine($"  模式: {logic.Pattern}");
        sb.AppendLine($"  参数: {logic.Parameters}");
        sb.AppendLine($"  置信度: {logic.Confidence:F2}");
        sb.AppendLine($"  抽象步骤:");
        for (var i = 0; i < logic.Steps.Count; i++)
            sb.AppendLine($"    [{i + 1}] {logic.Steps[i]}");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>步骤优化（L-04）— 分析操作逻辑并提出优化建议</summary>
    [McpTool("optimize_steps", "分析操作逻辑并提出优化建议(合并冗余/缩短等待/替代操作)", "desktop")]
    public async Task<ToolResult> OptimizeStepsAsync(
        [McpToolParameter("操作模式名称", Required = true)] string name,
        [McpToolParameter("操作模式描述", Required = true)] string pattern,
        [McpToolParameter("参数化描述", Required = false)] string? parameters = null,
        [McpToolParameter("步骤列表(分号分隔)", Required = false)] string? steps = null,
        CancellationToken ct = default)
    {
        var stepList = string.IsNullOrEmpty(steps)
            ? []
            : steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var logic = new AbstractOperationLogic(name, pattern, parameters ?? string.Empty, stepList, 0.5);
        var suggestion = await _learner.OptimizeAsync(logic, ct).ConfigureAwait(false);

        return ToolResultBuilder.Success().WithText(suggestion).Build();
    }
}
