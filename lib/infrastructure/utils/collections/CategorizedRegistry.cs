namespace Core.Utils;

/// <summary>
/// 分类注册表 — 在 CachedRegistry 基础上为每个键关联分类标签，支持启用/禁用过滤和缓存枚举
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
/// <typeparam name="TValue">值类型</typeparam>
/// <typeparam name="TCategory">分类类型</typeparam>
public sealed class CategorizedRegistry<TKey, TValue, TCategory> where TKey : notnull
{
    private readonly CachedRegistry<TKey, TValue> _registry;
    private readonly Dictionary<TKey, TCategory> _categories;
    private readonly Func<TValue, bool>? _isEnabled;
    private readonly TCategory _defaultCategory;
    private IReadOnlyList<CategorizedEntry<TKey, TValue, TCategory>> _cachedEntries = [];
    private bool _cachedEntriesValid;

    /// <summary>
    /// 获取已注册条目数量
    /// </summary>
    public int Count => _registry.Count;

    /// <summary>
    /// 构造函数 — 指定默认分类、启用判断回调和键比较器
    /// </summary>
    /// <param name="defaultCategory">未显式分类时使用的默认分类</param>
    /// <param name="isEnabled">值启用判断回调，为 null 时全部启用</param>
    /// <param name="comparer">键比较器，可为 null</param>
    public CategorizedRegistry(
        TCategory defaultCategory,
        Func<TValue, bool>? isEnabled = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        _registry = new CachedRegistry<TKey, TValue>(comparer);
        _categories = new Dictionary<TKey, TCategory>(comparer ?? EqualityComparer<TKey>.Default);
        _isEnabled = isEnabled;
        _defaultCategory = defaultCategory;
    }

    /// <summary>
    /// 注册规范名条目
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    /// <param name="isCanonical">是否为规范名，默认 true</param>
    public void Register(TKey key, TValue value, bool isCanonical = true)
    {
        _registry.Register(key, value, isCanonical);
        _cachedEntriesValid = false;
    }

    /// <summary>
    /// 注册别名条目
    /// </summary>
    /// <param name="alias">别名键</param>
    /// <param name="value">值</param>
    public void RegisterAlias(TKey alias, TValue value)
    {
        _registry.RegisterAlias(alias, value);
        _cachedEntriesValid = false;
    }

    /// <summary>
    /// 设置指定键的分类标签
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="category">分类</param>
    public void SetCategory(TKey key, TCategory category)
    {
        _categories[key] = category;
        _cachedEntriesValid = false;
    }

    /// <summary>
    /// 注销指定键
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>是否成功移除</returns>
    public bool Unregister(TKey key)
    {
        var removed = _registry.Unregister(key);
        if (removed) _cachedEntriesValid = false;
        return removed;
    }

    /// <summary>
    /// 尝试获取值，禁用条目返回 false
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">获取到的值</param>
    /// <returns>是否找到且启用</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_registry.TryGetValue(key, out value))
            return false;

        if (_isEnabled is not null && !_isEnabled(value))
        {
            value = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断是否包含指定键且对应值启用
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>是否包含且启用</returns>
    public bool ContainsKey(TKey key)
    {
        if (!_registry.TryGetValue(key, out var value))
            return false;

        return _isEnabled is null || _isEnabled(value);
    }

    /// <summary>
    /// 获取所有规范名键值对
    /// </summary>
    /// <returns>规范名键值字典</returns>
    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical() => _registry.GetAllCanonical();

    /// <summary>
    /// 遍历器 — 返回 IEnumerable，脏标记缓存数组，仅在变更时重建
    /// </summary>
    public IEnumerable<CategorizedEntry<TKey, TValue, TCategory>> GetCategorizedEntries()
    {
        if (!_cachedEntriesValid)
        {
            _cachedEntries = _registry.GetCanonicalEntries()
                .Select(kvp => new CategorizedEntry<TKey, TValue, TCategory>(
                    kvp.Key,
                    kvp.Value,
                    _categories.TryGetValue(kvp.Key, out var cat) ? cat : _defaultCategory,
                    _isEnabled is null || _isEnabled(kvp.Value)))
                .ToArray();
            _cachedEntriesValid = true;
        }
        return _cachedEntries;
    }
}

/// <summary>
/// 分类注册表条目 — 包含键、值、分类和启用状态
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
/// <typeparam name="TValue">值类型</typeparam>
/// <typeparam name="TCategory">分类类型</typeparam>
public sealed record CategorizedEntry<TKey, TValue, TCategory>(TKey Key, TValue Value, TCategory Category, bool IsEnabled);
