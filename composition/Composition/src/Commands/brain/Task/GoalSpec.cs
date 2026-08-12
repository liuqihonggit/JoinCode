namespace JoinCode.ChatCommands;

/// <summary>
/// 目标规格 — 结构化目标定义，由 LLM 向用户收集 6 个字段后生成。
/// </summary>
public sealed record GoalSpec
{
    /// <summary>目标 (Outcome)：最终要达成的具体状态，最好有数字指标。</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    /// <summary>验证方式 (Verification)：用什么命令或指标来证明完成。</summary>
    [JsonPropertyName("verification")]
    public string Verification { get; init; } = string.Empty;

    /// <summary>硬性约束 (Constraints)：整个过程中绝不能打破的底线。</summary>
    [JsonPropertyName("constraints")]
    public string Constraints { get; init; } = string.Empty;

    /// <summary>工作边界 (Boundaries)：允许修改的文件或工具范围。</summary>
    [JsonPropertyName("boundaries")]
    public string Boundaries { get; init; } = string.Empty;

    /// <summary>迭代与记录 (IterationLog)：每次尝试后记录改动和结果。</summary>
    [JsonPropertyName("iterationLog")]
    public string IterationLog { get; init; } = string.Empty;

    /// <summary>失败熔断 (FailureCircuit)：遇到特定障碍无法推进时停止并报告。</summary>
    [JsonPropertyName("failureCircuit")]
    public string FailureCircuit { get; init; } = string.Empty;
}

/// <summary>
/// GoalSpec JSON 序列化上下文 — NativeAOT 兼容，复用 LlmJsonHelper 宽容反序列化。
/// </summary>
[JsonSerializable(typeof(GoalSpec))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
public partial class GoalSpecJsonContext : JsonSerializerContext;

/// <summary>
/// 解析 LLM 输出的 GoalSpec JSON — 复用 LlmJsonHelper 统一门控（ExtractJsonBlock + RepairJson + 宽容反序列化）。
/// </summary>
internal static class GoalSpecParser
{
    /// <summary>
    /// 尝试从 LLM 输出文本中解析 GoalSpec，失败返回 null。
    /// </summary>
    /// <param name="llmOutput">LLM 输出文本（可能包含 ```json 代码块或内联 JSON）。</param>
    /// <param name="logger">可选日志器。</param>
    /// <returns>解析成功的 GoalSpec，失败返回 null。</returns>
    public static GoalSpec? TryParse(string? llmOutput, ILogger? logger = null)
    {
        var result = LlmJsonHelper.Deserialize(llmOutput, GoalSpecJsonContext.Default.GoalSpec, out _, logger);
        if (result is null) return null;
        return result with
        {
            Outcome = result.Outcome ?? string.Empty,
            Verification = result.Verification ?? string.Empty,
            Constraints = result.Constraints ?? string.Empty,
            Boundaries = result.Boundaries ?? string.Empty,
            IterationLog = result.IterationLog ?? string.Empty,
            FailureCircuit = result.FailureCircuit ?? string.Empty,
        };
    }
}

/// <summary>
/// 构造 GoalSpec 收集 prompt — 要求 LLM 向用户逐个询问 6 个字段并输出 JSON。
/// </summary>
internal static class GoalSpecPromptBuilder
{
    /// <summary>
    /// 构造要求 LLM 向用户收集 GoalSpec 的 prompt。
    /// </summary>
    /// <param name="initialHint">用户提供的初始目标提示（可为空）。</param>
    /// <param name="presetConstraints">预填约束列表（可为空）。</param>
    /// <returns>完整的 GoalSpec 收集 prompt。</returns>
    public static string Build(string? initialHint = null, IReadOnlyList<string>? presetConstraints = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你需要向用户收集目标规格（GoalSpec），逐个询问以下 6 个字段。");
        sb.AppendLine("每问完一个字段，等待用户回答后再问下一个。");
        sb.AppendLine("所有字段收集完成后，输出一个 JSON 对象作为目标规格，然后以此规格开始自主工作。");
        sb.AppendLine();
        sb.AppendLine("## 字段说明");
        sb.AppendLine();
        sb.AppendLine("1. **目标 (Outcome)**：最终要达成的具体状态，最好有数字指标，如 p95 延迟降到 120ms 以下");
        sb.AppendLine("2. **验证方式 (Verification)**：用什么命令或指标来证明完成，如 `npm test` 必须全通过");
        sb.AppendLine("3. **硬性约束 (Constraints)**：整个过程中绝不能打破的底线，如不能改 `auth` 目录外的文件");
        sb.AppendLine("4. **工作边界 (Boundaries)**：允许修改的文件或工具范围");
        sb.AppendLine("5. **迭代与记录 (IterationLog)**：每次尝试后记录改动和结果（如更新 `EXPERIMENTS.md`）");
        sb.AppendLine("6. **失败熔断 (FailureCircuit)**：如果遇到特定障碍无法推进，请停止并报告已尝试的路径和原因");
        sb.AppendLine();
        sb.AppendLine("## JSON Schema");
        sb.AppendLine();
        sb.AppendLine("收集完所有字段后，输出以下格式的 JSON：");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"outcome\": \"...\",");
        sb.AppendLine("  \"verification\": \"...\",");
        sb.AppendLine("  \"constraints\": \"...\",");
        sb.AppendLine("  \"boundaries\": \"...\",");
        sb.AppendLine("  \"iterationLog\": \"...\",");
        sb.AppendLine("  \"failureCircuit\": \"...\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(initialHint))
        {
            sb.AppendLine("## 用户初始目标提示");
            sb.AppendLine();
            sb.AppendLine(initialHint);
            sb.AppendLine("请据此向用户细化询问各字段。");
            sb.AppendLine();
        }

        if (presetConstraints is { Count: > 0 })
        {
            sb.AppendLine("## 预填约束");
            sb.AppendLine();
            sb.AppendLine("已知约束：");
            foreach (var c in presetConstraints)
            {
                sb.AppendLine($"- {c}");
            }
            sb.AppendLine("这些约束已预填，询问 Constraints 字段时可确认或补充。");
            sb.AppendLine();
        }

        sb.AppendLine("## 执行流程");
        sb.AppendLine();
        sb.AppendLine("1. 逐个向用户询问上述字段");
        sb.AppendLine("2. 收集完所有字段后，输出 JSON");
        sb.AppendLine("3. 以此 JSON 作为目标规格，开始自主工作以达成目标");

        return sb.ToString();
    }
}
