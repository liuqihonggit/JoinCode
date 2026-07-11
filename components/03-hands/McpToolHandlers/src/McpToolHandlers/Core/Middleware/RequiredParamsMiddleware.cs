
namespace McpToolRegistry;

/// <summary>
/// 必填参数校验中间件 — Order=200 — 检查必填参数是否提供
/// </summary>
[Register]
public sealed partial class RequiredParamsMiddleware : IToolExecutionMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    [Inject] private readonly ILogger<RequiredParamsMiddleware> _logger;

    public RequiredParamsMiddleware(ILogger<RequiredParamsMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (context.Handler is not null)
        {
            var validationResult = ValidateRequiredParameters(
                context.ToolName, context.Handler, context.Arguments);
            if (validationResult != null)
            {
                _logger.LogWarning(L.T(StringKey.ToolParamsMissingLog, context.ToolName, validationResult));
                context.Span?.SetStatus(TelemetryStatusCode.Error, "Missing parameters");
                context.Result = CreateParameterMissingResult(context.ToolName, validationResult);
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private static string? ValidateRequiredParameters(
        string toolName,
        IToolHandler handler,
        Dictionary<string, JsonElement> arguments)
    {
        var schema = handler.InputSchema;
        if (schema.Required == null || schema.Required.Count == 0)
        {
            return null;
        }

        var missingParams = new List<string>();

        foreach (var requiredParam in schema.Required)
        {
            if (!arguments.ContainsKey(requiredParam))
            {
                missingParams.Add(requiredParam);
            }
        }

        if (missingParams.Count == 0)
        {
            return null;
        }

        var details = new List<string>();
        foreach (var param in missingParams)
        {
            if (schema.Properties.TryGetValue(param, out var prop))
            {
                var hasDefault = !string.IsNullOrEmpty(prop.Default);
                var desc = string.IsNullOrEmpty(prop.Description) ? "" : $" ({prop.Description})";
                var defaultInfo = hasDefault ? L.T(StringKey.DefaultValueLabel, prop.Default) : L.T(StringKey.NoDefaultValue);
                details.Add($"- {param}{desc}{defaultInfo}");
            }
            else
            {
                details.Add(L.T(StringKey.UnknownPropertyMustProvide, param));
            }
        }

        return string.Join("\n", details);
    }

    private static ToolResult CreateParameterMissingResult(string toolName, string missingDetails)
    {
        var message = L.T(StringKey.MissingRequiredParams, toolName, missingDetails);

        return new ToolResult
        {
            Content =
            [
                new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = message
                }
            ],
            IsError = true
        };
    }
}
