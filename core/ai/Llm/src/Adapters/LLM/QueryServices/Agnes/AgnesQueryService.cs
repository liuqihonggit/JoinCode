namespace Api.LLM.QueryServices.Agnes;


/// <summary>
/// Agnes AI QueryService — Agnes 协议与 OpenAI 基本兼容，但存在以下严格校验差异：
///
/// 【踩坑1】tools[].function.parameters 字段不可省略
///   OpenAI API 允许无参数工具省略 parameters 字段（默认为空对象），
///   但 Agnes 严格校验 JSON 结构，缺少 parameters 字段返回：
///   "Invalid JSON data: missing field `parameters` at line 1 column N"
///   修复：BuildParameters 无参数时返回空 OpenAIFunctionParameters()（序列化为 {"type":"object"}），
///   而非 null。此修复已在基类 BuildParameters 中完成，AgnesQueryService 额外做防御性后处理。
///
/// 【踩坑2】stream_options 字段
///   Agnes 文档未列出 stream_options，但实测发送 stream_options 不会报错，
///   且能正常返回 usage 字段。因此不需要跳过，基类统一发送即可。
///
/// 【踩坑3】frequency_penalty / presence_penalty / reasoning_effort
///   Agnes 文档未列出这些参数，但它们有 JsonIgnore(WhenWritingNull) 保护，
///   仅在非 null 时发送。默认 ChatOptions 不设置这些值，所以不会发送。
///   如未来需要，可通过 IProviderDefinition 新增能力标记控制。
///
/// 【踩坑4】请求体大小
///   jcc 发送的 system prompt + 工具定义约 200KB，Agnes 可正常处理（512K 上下文窗口）。
///   但部分小型 OpenAI 兼容供应商可能对请求体大小有限制，需注意。
/// </summary>
public sealed class AgnesQueryService : OpenAIQueryService
{
    public AgnesQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null, ResilientHttpExecutor? resilientExecutor = null)
        : base(config, httpClient, logger, fs, resilientExecutor) { }

    /// <summary>
    /// 覆写 CreateRequest — 对基类结果做 Agnes 专属后处理：
    /// 防御性确保每个 tool.function.parameters 非空（踩坑1的兜底保护）
    /// </summary>
    internal override OpenAIChatRequest CreateRequest(MessageList chatHistory, ChatOptions? settings, bool stream, IChatClient? kernel)
    {
        var request = base.CreateRequest(chatHistory, settings, stream, kernel);

        if (request.Tools is { Count: > 0 })
        {
            foreach (var tool in request.Tools)
            {
                if (tool.Function is not null && tool.Function.Parameters is null)
                {
                    tool.Function.Parameters = new OpenAIFunctionParameters();
                }
            }
        }

        return request;
    }
}
