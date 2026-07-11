
namespace Core.Agents;

/// <summary>
/// 通用 Agent - 处理通用任务
/// </summary>
public sealed class GeneralPurposeAgent : BuiltInAgentBase
{
    public override string Name => "GeneralPurposeAgent";
    public override string Description => "处理各种通用任务，提供信息查询、代码辅助、文本生成等功能";
    public override BuiltInAgentType AgentType => BuiltInAgentType.GeneralPurpose;
    public override string SystemPrompt => AgentPrompts.GeneralPurposeAgentSystemPrompt;

    public GeneralPurposeAgent(
        IChatClient kernel,
        IClockService clock,
        ILogger<GeneralPurposeAgent>? logger = null)
        : base(kernel, clock, logger)
    {
    }

    /// <summary>
    /// 执行通用任务
    /// </summary>
    public async Task<GeneralTaskResult> ExecuteTaskAsync(
        GeneralTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildTaskPrompt(request);
        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GeneralTaskResult
        {
            Success = true,
            TaskId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 回答信息查询
    /// </summary>
    public async Task<GeneralTaskResult> AnswerQueryAsync(
        string query,
        QueryType queryType = QueryType.General,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请回答以下{GetQueryTypeDescription(queryType)}查询：

## 查询
{query}

请提供：
1. 直接回答
2. 相关背景信息
3. 补充说明（如适用）
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GeneralTaskResult
        {
            Success = true,
            TaskId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 生成内容
    /// </summary>
    public async Task<GeneralTaskResult> GenerateContentAsync(
        ContentGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请生成以下内容：

## 内容类型
{GetContentTypeDescription(request.ContentType)}

## 主题/要求
{request.Topic}

{(string.IsNullOrWhiteSpace(request.Style) ? "" : $"## 风格要求\n{request.Style}\n")}
{(string.IsNullOrWhiteSpace(request.Length) ? "" : $"## 长度要求\n{request.Length}\n")}
{(request.KeyPoints == null || request.KeyPoints.Count == 0 ? "" : $"## 必须包含的要点\n{string.Join("\n", request.KeyPoints.Select(p => $"- {p}"))}\n")}

请确保内容：
- 准确且有用
- 结构清晰
- 语言流畅
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GeneralTaskResult
        {
            Success = true,
            TaskId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 协助问题解决
    /// </summary>
    public async Task<GeneralTaskResult> AssistProblemSolvingAsync(
        string problem,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请协助解决以下问题：

## 问题描述
{problem}

{(string.IsNullOrWhiteSpace(context) ? "" : $"## 背景上下文\n{context}\n")}

请提供：
1. 问题分析
2. 可能的解决方案（多种）
3. 每种方案的优缺点
4. 推荐方案及理由
5. 实施步骤
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GeneralTaskResult
        {
            Success = true,
            TaskId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    private static string BuildTaskPrompt(GeneralTaskRequest request)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请执行以下任务：");
        prompt.AppendLine();
        prompt.AppendLine($"## 任务描述\n{request.TaskDescription}");

        if (!string.IsNullOrWhiteSpace(request.Input))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 输入内容\n{request.Input}");
        }

        if (request.Constraints != null && request.Constraints.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 约束条件");
            foreach (var constraint in request.Constraints)
            {
                prompt.AppendLine($"- {constraint}");
            }
        }

        if (request.ExpectedOutput != null)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 期望输出格式");
            prompt.AppendLine(request.ExpectedOutput switch
            {
                ExpectedOutputFormat.Text => "纯文本",
                ExpectedOutputFormat.Json => "JSON 格式",
                ExpectedOutputFormat.Markdown => "Markdown 格式",
                ExpectedOutputFormat.List => "列表形式",
                ExpectedOutputFormat.Table => "表格形式",
                _ => "根据内容自动选择"
            });
        }

        prompt.AppendLine();
        prompt.AppendLine("请根据任务要求提供高质量的输出。");

        return prompt.ToString();
    }

    private static string GetQueryTypeDescription(QueryType queryType) => queryType switch
    {
        QueryType.Technical => "技术",
        QueryType.Conceptual => "概念",
        QueryType.HowTo => "操作指南",
        QueryType.Comparison => "对比",
        QueryType.BestPractice => "最佳实践",
        _ => "一般"
    };

    private static string GetContentTypeDescription(ContentType contentType) => contentType switch
    {
        ContentType.Documentation => "文档",
        ContentType.Code => "代码",
        ContentType.Explanation => "解释说明",
        ContentType.Summary => "摘要总结",
        ContentType.Analysis => "分析报告",
        ContentType.Proposal => "方案建议",
        _ => "通用内容"
    };

    protected override float GetTemperature() => 0.7f;
}

/// <summary>
/// 通用任务请求
/// </summary>
public sealed record GeneralTaskRequest
{
    public required string TaskDescription { get; init; }
    public string? Input { get; init; }
    public List<string>? Constraints { get; init; }
    public ExpectedOutputFormat? ExpectedOutput { get; init; }
}

/// <summary>
/// 内容生成请求
/// </summary>
public sealed record ContentGenerationRequest
{
    public required ContentType ContentType { get; init; }
    public required string Topic { get; init; }
    public string? Style { get; init; }
    public string? Length { get; init; }
    public List<string>? KeyPoints { get; init; }
}

/// <summary>
/// 查询类型
/// </summary>
public enum QueryType
{
    [EnumValue("general")] General,
    [EnumValue("technical")] Technical,
    [EnumValue("conceptual")] Conceptual,
    [EnumValue("howTo")] HowTo,
    [EnumValue("comparison")] Comparison,
    [EnumValue("bestPractice")] BestPractice
}

/// <summary>
/// 内容类型
/// </summary>
public enum ContentType
{
    [EnumValue("documentation")] Documentation,
    [EnumValue("code")] Code,
    [EnumValue("explanation")] Explanation,
    [EnumValue("summary")] Summary,
    [EnumValue("analysis")] Analysis,
    [EnumValue("proposal")] Proposal
}

/// <summary>
/// 期望输出格式
/// </summary>
public enum ExpectedOutputFormat
{
    [EnumValue("text")] Text,
    [EnumValue("json")] Json,
    [EnumValue("markdown")] Markdown,
    [EnumValue("list")] List,
    [EnumValue("table")] Table
}

/// <summary>
/// 通用任务结果
/// </summary>
public sealed record GeneralTaskResult
{
    public required bool Success { get; init; }
    public string? TaskId { get; init; }
    public string? Content { get; init; }
    public long ExecutionTimeMs { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
}
