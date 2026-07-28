
namespace Core.Prompts.Utils;

[Register(typeof(ISynonymMap))]
public sealed partial class SynonymMap : ISynonymMap
{
    private readonly FrozenDictionary<string, string> _map;

    public SynonymMap() : this(GetDefaultMap()) { }

    public SynonymMap(IDictionary<string, string> map)
    {
        _map = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Entries => _map;

    public bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        return _map.TryGetValue(key, out value);
    }

    public bool ContainsKey(string key) => _map.ContainsKey(key);

    /// <summary>
    /// 同义词转换词典
    /// </summary>
    private static IDictionary<string, string> GetDefaultMap()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
        };
    }
}
