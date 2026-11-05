namespace Infrastructure.HotSpot;

/// <summary>
/// 调用点查找器实现 — 通过回调注入实际查找逻辑（CodeSemanticSearch/grep）
/// 秘书用此工具找调用点后批量改
/// </summary>
[Register(typeof(ICallSiteFinder), ServiceLifetime.Singleton)]
public sealed class CallSiteFinder : ICallSiteFinder {
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<CodeCallSite>>> _searchFunc;

    /// <summary>
    /// 构造函数 — 注入实际查找逻辑回调
    /// </summary>
    /// <param name="searchFunc">查找回调 (符号名, 搜索根, 取消令牌) → 调用点列表</param>
    public CallSiteFinder(Func<string, string, CancellationToken, Task<IReadOnlyList<CodeCallSite>>> searchFunc) {
        _searchFunc = searchFunc ?? throw new ArgumentNullException(nameof(searchFunc));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CodeCallSite>> FindCallSitesAsync(string symbolName, string searchRoot, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchRoot);
        return _searchFunc(symbolName, searchRoot, cancellationToken);
    }
}