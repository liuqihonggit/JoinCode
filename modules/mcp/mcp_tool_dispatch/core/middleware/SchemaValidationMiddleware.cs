
namespace McpToolRegistry;

/// <summary>
/// Schema 校验中间件 — Order=300 — 验证工具参数是否符合 InputSchema
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SchemaValidationMiddleware : ServiceEntity, IToolExecutionMiddleware {

    private readonly IJsonSchemaValidator? _schemaValidator;
    private readonly ILogger<SchemaValidationMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入 JSON Schema 校验器和日志记录器
    /// </summary>
    /// <param name="schemaValidator">JSON Schema 校验器实例，为 null 则跳过校验</param>
    /// <param name="logger">日志记录器实例</param>
    public SchemaValidationMiddleware(
        IJsonSchemaValidator? schemaValidator,
        ILogger<SchemaValidationMiddleware> logger) {
        _schemaValidator = schemaValidator;
        _logger = logger;
    }

    /// <summary>
    /// 校验工具参数是否符合 Handler 的 InputSchema；校验失败则设置遥测错误状态并返回带格式化错误信息的错误结果，否则调用下一层中间件
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct) {
        if (_schemaValidator is not null && context.Handler is not null) {
            var schema = context.Handler.InputSchema;
            var schemaJson = JsonSerializer.Serialize(schema, ContractsJsonContext.Default.ToolSchema);
            var argsJson = JsonSerializer.Serialize(context.Arguments, ContractsJsonContext.Default.DictionaryStringJsonElement);

            var validation = _schemaValidator.Validate(argsJson, schemaJson);
            if (!validation.IsValid) {
                var formatted = InputSchemaValidationFormatter.FormatErrors(context.ToolName, validation.Errors);
                _logger.LogWarning("Tool {ToolName} input schema validation failed: {Errors}",
                    context.ToolName, formatted);
                context.Span?.SetStatus(TelemetryStatusCode.Error, "Schema validation failed");
                context.Result = new ToolResult {
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