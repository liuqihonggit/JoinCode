namespace Structura.Primitives;

/// <summary>
/// 通用命名值对 — 替代各处重复的 Name+Value / Key+Value 模式
/// </summary>
public sealed record NamedValue<T>(string Name, T Value)
{
    /// <summary>从键值对构造字符串命名值对</summary>
    /// <param name="kvp">源键值对</param>
    /// <returns>键为 Name、值为 Value 的命名值对</returns>
    public static NamedValue<string> FromKeyValuePair(KeyValuePair<string, string> kvp)
        => new(kvp.Key, kvp.Value);

    /// <summary>转换为键值对</summary>
    /// <returns>键为 Name、值为 Value 的键值对</returns>
    public KeyValuePair<string, T> ToKeyValuePair() => new(Name, Value);
}

/// <summary>
/// 字符串命名值对简写 — 最常见的 Name+Value 场景
/// </summary>
public sealed record NamedValue(string Name, string Value);
