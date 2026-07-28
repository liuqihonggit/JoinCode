namespace Structura.Primitives;

/// <summary>
/// 通用命名值对 — 替代各处重复的 Name+Value / Key+Value 模式
/// </summary>
public sealed record NamedValue<T>(string Name, T Value)
{
    public static NamedValue<string> FromKeyValuePair(KeyValuePair<string, string> kvp)
        => new(kvp.Key, kvp.Value);

    public KeyValuePair<string, T> ToKeyValuePair() => new(Name, Value);
}

/// <summary>
/// 字符串命名值对简写 — 最常见的 Name+Value 场景
/// </summary>
public sealed record NamedValue(string Name, string Value);
