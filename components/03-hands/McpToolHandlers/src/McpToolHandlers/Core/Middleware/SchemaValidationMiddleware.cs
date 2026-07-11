
namespace McpToolRegistry;

/// <summary>
/// Schema 校验中间件 — Order=300 — 验证工具参数是否符合 InputSchema
/// </summary>
[Register]
public sealed partial class SchemaValidationMiddleware : IToolExecutionMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    private readonly IJsonSchemaValidator? _schemaValidator;
    [Inject] private readonly ILogger<SchemaValidationMiddleware> _logger;

    public SchemaValidationMiddleware(
        IJsonSchemaValidator? schemaValidator,
        ILogger<SchemaValidationMiddleware> logger)
    {
        _schemaValidator = schemaValidator;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_schemaValidator is not null && context.Handler is not null)
        {
            var schema = context.Handler.InputSchema;
            var schemaJson = JsonSerializer.Serialize(schema, ContractsJsonContext.Default.ToolSchema);
            var argsJson = JsonSerializer.Serialize(context.Arguments, ContractsJsonContext.Default.DictionaryStringJsonElement);

            var validation = _schemaValidator.Validate(argsJson, schemaJson);
            if (!validation.IsValid)
            {
                var formatted = InputSchemaValidationFormatter.FormatErrors(context.ToolName, validation.Errors);
                _logger.LogWarning("Tool {ToolName} input schema validation failed: {Errors}",
                    context.ToolName, formatted);
                context.Span?.SetStatus(TelemetryStatusCode.Error, "Schema validation failed");
                context.Result = new ToolResult
                {
                    Content =
                    [
                        new ToolContent
                        {
                            Type = ToolContentType.Text,
                            Text = $"<tool_use_error>InputValidationError: {formatted}</tool_use_error>"
                        }
                    ],
                    IsError = true
                };
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
