
namespace Core.Prompts.Utils;

/// <summary>
/// 同义词映射表 — 将用户输入的同义词归一化到标准键，支持大小写不敏感查找。
/// </summary>
[Register(typeof(ISynonymMap), ServiceLifetime.Singleton)]
public sealed partial class SynonymMap : ServiceEntity, ISynonymMap {
    private readonly FrozenDictionary<string, string> _map;

    /// <summary>
    /// 使用默认同义词词典初始化实例。
    /// </summary>
    public SynonymMap() : this(GetDefaultMap()) { }

    /// <summary>
    /// 使用指定字典初始化实例，内部转为大小写不敏感的冻结字典。
    /// </summary>
    /// <param name="map">同义词源字典，键为同义词，值为标准词。</param>
    public SynonymMap(IDictionary<string, string> map) {
        _map = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 同义词条目数量
    /// </summary>
    public int Count => _map.Count;

    /// <summary>
    /// 获取所有同义词条目的快照 — 用于遍历
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllEntries() => _map;

    /// <summary>
    /// 尝试获取指定键对应的标准词。
    /// </summary>
    /// <param name="key">待查同义词。</param>
    /// <param name="value">匹配成功时输出标准词，否则为 null。</param>
    /// <returns>存在映射返回 true，否则 false。</returns>
    public bool TryGetValue(string key, [NotNullWhen(true)] out string? value) {
        return _map.TryGetValue(key, out value);
    }

    /// <summary>
    /// 判断是否包含指定同义词键。
    /// </summary>
    /// <param name="key">待查同义词。</param>
    /// <returns>包含返回 true，否则 false。</returns>
    public bool ContainsKey(string key) => _map.ContainsKey(key);

    /// <summary>
    /// 同义词转换词典
    /// </summary>
    private static IDictionary<string, string> GetDefaultMap() {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        };
    }
}