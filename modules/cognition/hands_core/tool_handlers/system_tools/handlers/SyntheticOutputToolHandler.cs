namespace Tools.Handlers;

/// <summary>
/// 结构化输出合成工具 — 对齐 TS SyntheticOutputTool.ts
/// 当 --json-schema 参数提供时，动态创建此工具，LLM 调用时验证输出是否符合 Schema
/// 验证通过 → ToolResult.StructuredOutput 填充验证数据，QueryEngine 捕获到 result.structured_output
/// 验证失败 → ToolResult.IsError=true + 错误信息，LLM 收到后可修正重试（上限5次）
/// </summary>
public sealed class SyntheticOutputToolHandler {
    private readonly SimpleJsonSchemaValidator _validator;
    private readonly string _schemaJson;

    /// <summary>
    /// 构造合成工具 — Schema 在构造时绑定，对齐 TS createSyntheticOutputTool(jsonSchema)
    /// </summary>
    /// <param name="validator">JSON Schema 验证器</param>
    /// <param name="schemaJson">JSON Schema 定义字符串</param>
    public SyntheticOutputToolHandler(
        SimpleJsonSchemaValidator validator,
        string schemaJson) {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _schemaJson = schemaJson ?? throw new ArgumentNullException(nameof(schemaJson));
    }

    /// <summary>
    /// 验证 LLM 输出是否符合注册的 JSON Schema
    /// 对齐 TS SyntheticOutputTool.call(input) — 验证通过返回 structured_output，失败返回 is_error tool_result
    /// </summary>
    public Task<ToolResult> ProvideStructuredOutputAsync(
        string input,
        CancellationToken cancellationToken = default) {
        try {
            var result = _validator.Validate(input, _schemaJson);

            if (result.IsValid) {
                var jsonElement = JsonDocument.Parse(input).RootElement.Clone();
                return Task.FromResult(ToolResultBuilder.Success()
                    .WithText("Structured output provided successfully")
                    .Build() with { StructuredOutput = jsonElement });
            }

            var errorMessages = string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}"));
            var diag = ToolDiagnostic.Create(
                reason: "Schema validation failed",
                formattedMessage: !string.IsNullOrEmpty(errorMessages)
                    ? $"Output does not match required schema: {errorMessages}"
                    : "Output does not match required schema",
                details: result.Errors.Select(e => new DiagnosticDetail(e.Path, e.Message)).ToList(),
                suggestions: ["Fix the output to match the JSON Schema and try again"]);
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        } catch (JsonException ex) {
            var diag = ToolDiagnostic.Create(
                reason: "Invalid JSON input",
                formattedMessage: $"Failed to parse input as JSON: {ex.Message}",
                suggestions: ["Ensure the input is valid JSON conforming to the schema"]);
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }
    }
}
