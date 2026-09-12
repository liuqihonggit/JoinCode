namespace JoinCode.Abstractions.Collections;

/// <summary>
/// 字典只读视图 — 封装字典对外暴露，零拷贝查询/遍历。
/// <para>替代 <c>Keys.ToList()</c>/<c>Values.ToList()</c> 转换拷贝风格：Contains 走字典直查 O(1)，Keys/Values 走 IEnumerable 视图零分配。</para>
/// <para>组合优于继承：各业务类持有 DictionaryView 实例对外暴露，而非暴露原始 ConcurrentDictionary/Dictionary。</para>
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
/// <typeparam name="TValue">值类型</typeparam>
public sealed class DictionaryView<TKey, TValue>
    where TKey : notnull
{
    private readonly IReadOnlyDictionary<TKey, TValue> _source;

    /// <summary>
    /// 创建字典只读视图
    /// </summary>
    /// <param name="source">被封装的字典（ConcurrentDictionary/Dictionary/FrozenDictionary 均可）</param>
    public DictionaryView(IReadOnlyDictionary<TKey, TValue> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>
    /// 判断是否包含指定键 — O(1) 零分配，走字典直查
    /// </summary>
    public bool Contains(TKey key) => _source.TryGetValue(key, out _);

    /// <summary>
    /// 尝试获取值 — O(1) 零分配，走字典直查
    /// </summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _source.TryGetValue(key, out value);

    /// <summary>
    /// 通过键获取值 — 键不存在时抛 KeyNotFoundException
    /// </summary>
    public TValue this[TKey key] => _source[key];

    /// <summary>
    /// 键只读视图 — 零拷贝，直接走字典 Keys 枚举器
    /// </summary>
    public IEnumerable<TKey> Keys => _source.Keys;

    /// <summary>
    /// 值只读视图 — 零拷贝，直接走字典 Values 枚举器
    /// </summary>
    public IEnumerable<TValue> Values => _source.Values;

    /// <summary>
    /// 元素数量
    /// </summary>
    public int Count => _source.Count;
}
