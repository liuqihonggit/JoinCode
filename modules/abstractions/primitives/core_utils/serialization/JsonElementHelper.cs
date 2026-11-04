namespace JoinCode.Abstractions.Utils;

public static class JsonElementHelper {
    /// <summary>将字符串转换为 JsonElement。</summary>
    public static JsonElement FromString(string? value) {
        if (value is null)
            return NullElement();

        using var doc = JsonDocument.Parse($"\"{JsonEncodedText.Encode(value)}\"");
        return doc.RootElement.Clone();
    }

    /// <summary>将 int 转换为 JsonElement。</summary>
    public static JsonElement FromInt32(int value) {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    /// <summary>将 long 转换为 JsonElement。</summary>
    public static JsonElement FromInt64(long value) {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    /// <summary>将 double 转换为 JsonElement。</summary>
    public static JsonElement FromDouble(double value) {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    /// <summary>将 bool 转换为 JsonElement。</summary>
    public static JsonElement FromBoolean(bool value) {
        using var doc = JsonDocument.Parse(value ? "true" : "false");
        return doc.RootElement.Clone();
    }

    /// <summary>获取表示 null 的 JsonElement。</summary>
    public static JsonElement NullElement() {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }

    /// <summary>将指定类型对象序列化为 JsonElement。</summary>
    public static JsonElement FromObject<T>(T value, JsonTypeInfo<T> typeInfo) {
        return JsonSerializer.SerializeToElement(value, typeInfo);
    }

    /// <summary>
    /// 从原始 JSON 字符串解析为 JsonElement（不包裹引号）。
    /// 适用于 JSON 数组、JSON 对象等非标量值。
    /// </summary>
    public static JsonElement FromJson(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>将 JsonElement 对象转换为字典。</summary>
    public static Dictionary<string, JsonElement> ToDictionary(this JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Expected Object, got {element.ValueKind}");

        var dict = new Dictionary<string, JsonElement>();
        foreach (var property in element.EnumerateObject()) {
            dict[property.Name] = property.Value.Clone();
        }
        return dict;
    }

    /// <summary>获取字符串值,非字符串类型返回 null。</summary>
    public static string? GetStringOrNull(this JsonElement element) {
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary>尝试获取字符串值。</summary>
    public static bool TryGetString(this JsonElement element, out string? value) {
        if (element.ValueKind == JsonValueKind.String) {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>将基元类型对象转换为 JsonElement。</summary>
    public static JsonElement FromPrimitives(object? value) => value switch {
        string s => FromString(s),
        int i => FromInt32(i),
        long l => FromInt64(l),
        double d => FromDouble(d),
        bool b => FromBoolean(b),
        JsonElement je => je,
        null => NullElement(),
        _ => FromString(value.ToString())
    };

    /// <summary>根据键值对数组构造字典。</summary>
    public static Dictionary<string, JsonElement> Dict(params (string Key, JsonElement Value)[] pairs) {
        var dict = new Dictionary<string, JsonElement>(pairs.Length);
        foreach (var (key, val) in pairs)
            dict[key] = val;
        return dict;
    }
}
