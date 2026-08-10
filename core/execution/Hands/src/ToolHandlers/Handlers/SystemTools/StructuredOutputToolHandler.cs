namespace Tools.Handlers;

/// <summary>
/// 结构化输出工具处理器 - 提供JSON Schema注册与验证功能
/// 允许LLM请求输出符合JSON Schema的结构化数据
/// </summary>
[McpToolDispatch(ToolCategory.StructuredOutput)]
public sealed class StructuredOutputToolHandler
{
    private readonly SimpleJsonSchemaValidator _validator;
    private readonly ConcurrentDictionary<string, StructuredOutputSchema> _schemas = new();

    /// <summary>
    /// 验证结果缓存 — 对齐 TS WeakMap toolCache，避免重复编译同一 Schema
    /// </summary>
    private readonly ConcurrentDictionary<string, SchemaValidationResult> _validationCache = new();

    private static readonly JsonWriterOptions s_indentedWriterOptions = new() { Indented = true };

    public StructuredOutputToolHandler(SimpleJsonSchemaValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <summary>
    /// 注册JSON Schema用于结构化输出验证
    /// </summary>
    [McpTool(SystemToolNameConstants.StructuredOutputRegister, "Register JSON Schema for structured output validation", "structured_output")]
    public Task<ToolResult> RegisterSchemaAsync(
        [McpToolParameter("Schema name")] string schema_name,
        [McpToolParameter("JSON Schema definition (JSON format)")] string schema_json,
        [McpToolParameter("Schema description", Required = false)] string? description = null,
        [McpToolParameter("Strict mode (disallow additional properties), defaults to true", Required = false, DefaultValue = "true")] bool strict = true,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(schema_name, "schema_name"),
            ValidationHelper.ValidateRequired(schema_json, "schema_json"),
            ValidationHelper.ValidateStringLength(schema_name, 128, "schema_name"));
        if (validationError != null)
        {
            var diag = BuildValidationErrorDiagnostic(validationError);
            return Task.FromResult(ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        // 对齐 TS ajv.validateSchema(): 验证Schema结构合法性（不仅语法检查）
        var schemaValidation = _validator.ValidateSchema(schema_json);
        if (!schemaValidation.IsValid)
        {
            var errorMessages = string.Join("; ", schemaValidation.Errors.Select(e => $"{e.Path}: {e.Message}"));
            var diag = BuildInvalidSchemaDiagnostic(errorMessages);
            return Task.FromResult(ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        var schema = new StructuredOutputSchema
        {
            Name = schema_name,
            Description = description ?? string.Empty,
            SchemaJson = schema_json,
            Strict = strict
        };

        _schemas[schema_name] = schema;

        // 注册新 Schema 时清除该名称的缓存
        _validationCache.TryRemove(schema_name, out _);

        var response = new StringBuilder(256);
        response.AppendLine($"Schema registered: {schema_name}");
        if (!string.IsNullOrEmpty(description))
        {
            response.AppendLine($"Description: {description}");
        }
        response.AppendLine($"Strict mode: {(strict ? "Yes" : "No")}");

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 验证内容是否符合已注册的JSON Schema
    /// </summary>
    [McpTool(SystemToolNameConstants.StructuredOutputValidate, "Validate JSON content against a registered Schema, supports formatted output", "structured_output", ConcurrencySafe = true)]
    public Task<ToolResult> ValidateOutputAsync(
        [McpToolParameter("Registered Schema name")] string schema_name,
        [McpToolParameter("JSON content to validate")] string content,
        [McpToolParameter("Validate only without formatting, defaults to false", Required = false, DefaultValue = "false")] bool validate_only = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(schema_name, "schema_name"),
            ValidationHelper.ValidateRequired(content, "content"));
        if (validationError != null)
        {
            var diag = BuildValidationErrorDiagnostic(validationError);
            return Task.FromResult(ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        StructuredOutputSchema schema;
        if (!_schemas.TryGetValue(schema_name, out var found))
        {
            var diag = BuildSchemaNotFoundDiagnostic(schema_name);
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }
        schema = found;

        // 对齐 TS WeakMap toolCache: 使用缓存避免重复验证同一内容
        var cacheKey = $"{schema_name}:{content.GetHashCode(StringComparison.Ordinal)}";
        var result = _validationCache.GetOrAdd(cacheKey, _ => _validator.Validate(content, schema.SchemaJson));

        var response = new StringBuilder(512);
        response.AppendLine($"Schema: {schema_name}");
        response.AppendLine($"Validation result: {(result.IsValid ? "Passed" : "Failed")}");

        if (result.IsValid)
        {
            if (!validate_only)
            {
                // 格式化输出
                try
                {
                    var jsonNode = JsonNode.Parse(content);
                    string formattedJson;
                    if (jsonNode is not null)
                    {
                        using var stream = new MemoryStream();
                        using var writer = new Utf8JsonWriter(stream, s_indentedWriterOptions);
                        jsonNode.WriteTo(writer);
                        writer.Flush();
                        formattedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                    }
                    else
                    {
                        formattedJson = content;
                    }
                    response.AppendLine();
                    response.AppendLine("[Formatted output]");
                    response.AppendLine(formattedJson);
                }
                catch (JsonException)
                {
                    response.AppendLine("[Formatting failed, returning raw content]");
                    response.AppendLine(content);
                }
            }
        }
        else
        {
            response.AppendLine();
            response.AppendLine($"[Validation errors] ({result.Errors.Count})");
            foreach (var error in result.Errors)
            {
                response.AppendLine($"  Path: {error.Path} - {error.Message}");
            }
        }

        if (result.IsValid)
        {
            return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
        }
        else
        {
            var diag = BuildValidationFailedDiagnostic(schema_name, result.Errors.Count);
            return Task.FromResult(ToolResultBuilder.Error().WithText(response.ToString()).WithDiagnostic(diag).Build());
        }
    }

    internal static ToolDiagnostic BuildValidationErrorDiagnostic(string validationError) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: validationError,
            details: [new DiagnosticDetail("validation_error", validationError)]);

    internal static ToolDiagnostic BuildInvalidSchemaDiagnostic(string errorMessages) =>
        ToolDiagnostic.Create(
            reason: "Schema验证失败",
            formattedMessage: $"Invalid JSON Schema: {errorMessages}",
            details: [new DiagnosticDetail("errors", errorMessages)],
            suggestions: ["检查 JSON Schema 语法是否正确"]);

    internal static ToolDiagnostic BuildSchemaNotFoundDiagnostic(string schemaName) =>
        ToolDiagnostic.Create(
            reason: "Schema未找到",
            formattedMessage: $"Registered Schema not found: {schemaName}. Please register a Schema using structured_output_register first.",
            details: [new DiagnosticDetail("schema_name", schemaName)],
            suggestions: ["使用 structured_output_register 注册 Schema"]);

    internal static ToolDiagnostic BuildValidationFailedDiagnostic(string schemaName, int errorCount) =>
        ToolDiagnostic.Create(
            reason: "内容验证失败",
            formattedMessage: $"Validation failed against schema '{schemaName}' with {errorCount} error(s)",
            details:
            [
                new DiagnosticDetail("schema_name", schemaName),
                new DiagnosticDetail("error_count", errorCount.ToString())
            ]);
}
