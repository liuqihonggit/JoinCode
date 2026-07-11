namespace Core.Context.Compression;

/// <summary>
/// 压缩策略工厂接口
/// </summary>
public interface ICompressionStrategyFactory
{
    /// <summary>
    /// 获取适合的策略
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>压缩策略，如果没有找到则返回null</returns>
    ICompressionStrategy? GetStrategy(string content, ContentType contentType);

    /// <summary>
    /// 获取指定类型的所有策略
    /// </summary>
    /// <param name="contentType">内容类型</param>
    /// <returns>策略列表</returns>
    IEnumerable<ICompressionStrategy> GetStrategiesForType(ContentType contentType);

    /// <summary>
    /// 检查是否有适合的策略
    /// </summary>
    /// <param name="contentType">内容类型</param>
    /// <returns>是否有策略</returns>
    bool HasStrategyFor(ContentType contentType);

    /// <summary>
    /// 注册策略
    /// </summary>
    /// <param name="strategy">策略</param>
    void RegisterStrategy(ICompressionStrategy strategy);

    /// <summary>
    /// 注销策略
    /// </summary>
    /// <param name="strategyName">策略名称</param>
    /// <returns>是否成功注销</returns>
    bool UnregisterStrategy(string strategyName);

    /// <summary>
    /// 获取所有已注册的策略
    /// </summary>
    /// <returns>所有策略</returns>
    IEnumerable<ICompressionStrategy> GetAllStrategies();
}
