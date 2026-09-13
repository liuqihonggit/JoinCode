
namespace Core.Bridge.Handlers;

/// <summary>
/// 控制请求处理器抽象基类 — 提供 ControlRequest 处理的通用流程
/// </summary>
public abstract class ControlRequestHandlerBase : IMessageHandler
{
    /// <summary>消息类型标识</summary>
    public abstract string MessageType { get; }

    /// <summary>无效请求错误消息模板</summary>
    protected virtual string InvalidRequestMessage => $"Invalid {MessageType} request";

    /// <summary>
    /// 处理桥消息 — 校验类型后委托给 HandleActionAsync
    /// </summary>
    /// <param name="message">待处理的桥消息</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的桥消息（成功响应或错误消息）</returns>
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

    /// <summary>
    /// 处理具体控制动作 — 由派生类实现
    /// </summary>
    /// <param name="request">控制请求</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>控制响应</returns>
    protected abstract Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 创建成功响应
    /// </summary>
    /// <param name="request">原始请求</param>
    /// <param name="result">结果数据（可选）</param>
    /// <returns>成功控制响应</returns>
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

    /// <summary>
    /// 创建错误响应
    /// </summary>
    /// <param name="request">原始请求</param>
    /// <param name="error">错误消息</param>
    /// <returns>失败控制响应</returns>
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

    /// <summary>
    /// 从参数字典获取可选字符串值
    /// </summary>
    /// <param name="parameters">参数字典</param>
    /// <param name="key">参数键</param>
    /// <returns>字符串值；不存在时返回 null</returns>
    protected static string? GetOptionalString(Dictionary<string, JsonElement> parameters, string key)
    {
        return parameters.TryGetValue(key, out var element) ? element.GetString() : null;
    }

    /// <summary>
    /// 从参数字典获取必填字符串值
    /// </summary>
    /// <param name="parameters">参数字典</param>
    /// <param name="key">参数键</param>
    /// <returns>字符串值；不存在或为 null 时返回 string.Empty</returns>
    protected static string GetRequiredString(Dictionary<string, JsonElement> parameters, string key)
    {
        return parameters.TryGetValue(key, out var element) ? element.GetString() ?? string.Empty : string.Empty;
    }
}
