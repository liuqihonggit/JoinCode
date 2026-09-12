
namespace JoinCode.Abstractions.LLM;

/// <summary>
/// QueryService 工厂接口 — 按 ProviderKind 创建对应派生类实例
/// 唯一允许的 switch：构造决策点（非运行时协议分派）
/// </summary>
public interface IQueryServiceFactory
{
    /// <summary>
    /// 根据 ProviderConfig 创建对应的 IQueryService 实现
    /// Anthropic → AnthropicQueryService
    /// Azure → AzureQueryService
    /// OpenAI / Agnes（默认） → OpenAIQueryService
    /// </summary>
    IQueryService Create(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fileSystem = null);
}
