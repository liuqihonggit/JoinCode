namespace Tools.Handlers;

/// <summary>
/// 结构化输出工具处理器 - 提供JSON Schema注册与验证功能
/// 允许LLM请求输出符合JSON Schema的结构化数据
/// </summary>
[McpToolHandler(ToolCategory.StructuredOutput)]
public sealed class StructuredOutputToolHandler
{
    private readonly SimpleJsonSchemaValidator _validator;
    private readonly ConcurrentDictionary<string, StructuredOutputSchema> _schemas = new();

    /// <summary>
    /// 验证结果缓存 — 对齐 TS WeakMap toolCache，避免重复编译同一 Schema
    /// </summary>
    private readonly ConcurrentDictionary<string, SchemaValidationResult> _validationCache = new();

    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

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
            return Task.FromResult(ResultBuilder.Error().WithText(validationError).Build());
        }

        // 对齐 TS ajv.validateSchema(): 验证Schema结构合法性（不仅语法检查）
        var schemaValidation = _validator.ValidateSchema(schema_json);
        if (!schemaValidation.IsValid)
        {
            var errorMessages = string.Join("; ", schemaValidation.Errors.Select(e => $"{e.Path}: {e.Message}"));
            return Task.FromResult(ResultBuilder.Error().WithText($"Invalid JSON Schema: {errorMessages}").Build());
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

        return Task.FromResult(ResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 验证内容是否符合已注册的JSON Schema
    /// </summary>
    [McpTool(SystemToolNameConstants.StructuredOutputValidate, "Validate JSON content against a registered Schema, supports formatted output", "structured_output")]
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
            return Task.FromResult(ResultBuilder.Error().WithText(validationError).Build());
        }

        StructuredOutputSchema schema;
        if (!_schemas.TryGetValue(schema_name, out var found))
        {
            return Task.FromResult(ResultBuilder.Error()
                .WithText($"Registered Schema not found: {schema_name}. Please register a Schema using structured_output_register first.")
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
                    var formattedJson = jsonNode?.ToJsonString(s_indentedOptions) ?? content;
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

        return Task.FromResult(
            result.IsValid
                ? ResultBuilder.Success().WithText(response.ToString()).Build()
                : ResultBuilder.Error().WithText(response.ToString()).Build());
    }
}
