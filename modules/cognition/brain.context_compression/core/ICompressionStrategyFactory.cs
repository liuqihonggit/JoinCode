namespace Core.Context.Compression;

/// <summary>
/// 压缩策略工厂接口
/// </summary>
public interface ICompressionStrategyFactory {
    /// <summary>
    /// 获取适合的策略
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="strategy">压缩策略（找到时非 null）</param>
    /// <returns>true=找到适合策略；false=没有找到</returns>
    bool TryGetStrategy(string content, ContentType contentType, [NotNullWhen(true)] out ICompressionStrategy? strategy);

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