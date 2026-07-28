using JoinCode.Abstractions.Interfaces;

namespace McpClient;

[Register]
public sealed partial class InteractiveElicitationHandler : IElicitationHandler
{
    private readonly IUserInteractionService _userInteraction;
    [Inject] private readonly ILogger<InteractiveElicitationHandler>? _logger;
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public InteractiveElicitationHandler(
        IUserInteractionService userInteraction,
        ILogger<InteractiveElicitationHandler>? logger = null)
    {
        _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        _logger = logger;
    }

    public async Task<ElicitResult> HandleElicitationAsync(
        string serverName,
        JsonRpcId requestId,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mode = @params.Mode == ElicitModeConstants.Url ? ElicitModeConstants.Url : ElicitModeConstants.Form;

            _logger?.LogInformation("处理 Elicitation 请求: 服务器={ServerName}, 模式={Mode}", serverName, mode);

            return mode == ElicitModeConstants.Url
                ? await HandleUrlModeAsync(serverName, @params, cancellationToken).ConfigureAwait(false)
                : await HandleFormModeAsync(serverName, @params, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Elicitation 请求被取消: 服务器={ServerName}", serverName);
            return new ElicitResult { Action = ElicitActionConstants.Cancel };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 Elicitation 请求失败: 服务器={ServerName}", serverName);
            return new ElicitResult { Action = ElicitActionConstants.Cancel };
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private async Task<ElicitResult> HandleFormModeAsync(
        string serverName,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        var question = $"[MCP:{serverName}] {@params.Message}";

        if (@params.RequestedSchema?.Properties == null || @params.RequestedSchema.Properties.Count == 0)
        {
            var result = await _userInteraction.AskQuestionAsync(question, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrEmpty(result.Response))
            {
                return new ElicitResult { Action = ElicitActionConstants.Decline };
            }

            return new ElicitResult
            {
                Action = ElicitActionConstants.Accept,
                Content = new Dictionary<string, JsonElement?>
                {
                    ["response"] = JsonSerializer.SerializeToElement(result.Response, McpClientJsonContext.Default.String)
                }
            };
        }

        var content = new Dictionary<string, JsonElement?>();
        var requiredSet = @params.RequestedSchema.Required?.ToHashSet() ?? new HashSet<string>();

        foreach (var kvp in @params.RequestedSchema.Properties)
        {
            var fieldName = kvp.Key;
            var field = kvp.Value;
            var fieldLabel = !string.IsNullOrEmpty(field.Title) ? field.Title : fieldName;
            var fieldDesc = !string.IsNullOrEmpty(field.Description) ? $"\n  {field.Description}" : "";
            var isRequired = requiredSet.Contains(fieldName);
            var fieldQuestion = $"{question}\n  {fieldLabel}{fieldDesc}{(isRequired ? " (必填)" : " (可选)")}";

            List<string>? options = null;
            if (field.Enum is { Count: > 0 })
            {
                options = field.Enum;
            }

            var fieldResult = await _userInteraction.AskQuestionAsync(fieldQuestion, options, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!fieldResult.Success)
            {
                if (isRequired)
                {
                    return new ElicitResult { Action = ElicitActionConstants.Decline };
                }
                continue;
            }

            if (string.IsNullOrEmpty(fieldResult.Response) && isRequired)
            {
                return new ElicitResult { Action = ElicitActionConstants.Decline };
            }

            if (!string.IsNullOrEmpty(fieldResult.Response))
            {
                content[fieldName] = ConvertFieldValue(field.Type, fieldResult.Response);
            }
        }

        return new ElicitResult
        {
            Action = ElicitActionConstants.Accept,
            Content = content
        };
    }

    private async Task<ElicitResult> HandleUrlModeAsync(
        string serverName,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        var url = @params.Url ?? string.Empty;
        var message = $"[MCP:{serverName}] {@params.Message}\n  URL: {url}\n\n请在浏览器中完成操作后确认。";

        var confirmed = await _userInteraction.ConfirmAsync(message, cancellationToken).ConfigureAwait(false);

        if (!confirmed)
        {
            return new ElicitResult { Action = ElicitActionConstants.Decline };
        }

        return new ElicitResult { Action = ElicitActionConstants.Accept };
    }

    private static JsonElement? ConvertFieldValue(string fieldType, string value)
    {
        return fieldType switch
        {
            "number" or "integer" when double.TryParse(value, out var num) =>
                JsonSerializer.SerializeToElement(num, McpClientJsonContext.Default.Double),
            "boolean" when bool.TryParse(value, out var b) =>
                JsonSerializer.SerializeToElement(b, McpClientJsonContext.Default.String),
            _ => JsonSerializer.SerializeToElement(value, McpClientJsonContext.Default.String)
        };
    }
}