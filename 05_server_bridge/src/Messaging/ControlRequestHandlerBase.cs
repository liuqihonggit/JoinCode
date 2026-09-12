
namespace Core.Bridge.Handlers;

public abstract class ControlRequestHandlerBase : IMessageHandler
{
    public abstract string MessageType { get; }

    protected virtual string InvalidRequestMessage => $"Invalid {MessageType} request";

    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[{Handler}] 处理 {Type} 请求", GetType().Name, MessageType);

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = InvalidRequestMessage
            };
        }

        var parameters = request.GetParams();

        try
        {
            return await HandleActionAsync(request, parameters, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[{Handler}] {Type} 操作失败", GetType().Name, MessageType);
            return CreateErrorResponse(request, ex.Message);
        }
    }

    protected abstract Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken);

    protected static ControlResponse CreateSuccessResponse(ControlRequest request, JsonElement? result = null)
    {
        return new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = result
        };
    }

    protected static ControlResponse CreateErrorResponse(ControlRequest request, string error)
    {
        return new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = false,
            Error = error
        };
    }

    protected static string? GetOptionalString(Dictionary<string, JsonElement> parameters, string key)
    {
        return parameters.TryGetValue(key, out var element) ? element.GetString() : null;
    }

    protected static string GetRequiredString(Dictionary<string, JsonElement> parameters, string key)
    {
        return parameters.TryGetValue(key, out var element) ? element.GetString() ?? string.Empty : string.Empty;
    }
}
