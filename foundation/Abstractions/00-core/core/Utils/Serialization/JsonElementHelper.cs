namespace JoinCode.Abstractions.Utils;

public static class JsonElementHelper
{
    public static JsonElement FromString(string? value)
    {
        if (value is null)
            return NullElement();

        using var doc = JsonDocument.Parse($"\"{JsonEncodedText.Encode(value)}\"");
        return doc.RootElement.Clone();
    }

    public static JsonElement FromInt32(int value)
    {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    public static JsonElement FromInt64(long value)
    {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    public static JsonElement FromDouble(double value)
    {
        using var doc = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }

    public static JsonElement FromBoolean(bool value)
    {
        using var doc = JsonDocument.Parse(value ? "true" : "false");
        return doc.RootElement.Clone();
    }

    public static JsonElement NullElement()
    {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }

    public static JsonElement FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.SerializeToElement(value, typeInfo);
    }

    /// <summary>
    /// 从原始 JSON 字符串解析为 JsonElement（不包裹引号）。
    /// 适用于 JSON 数组、JSON 对象等非标量值。
    /// </summary>
    public static JsonElement FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static Dictionary<string, JsonElement> ToDictionary(this JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Expected Object, got {element.ValueKind}");

        var dict = new Dictionary<string, JsonElement>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }
        return dict;
    }

    public static string? GetStringOrNull(this JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    public static bool TryGetString(this JsonElement element, out string? value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    public static JsonElement FromPrimitives(object? value) => value switch
    {
        string s => FromString(s),
        int i => FromInt32(i),
        long l => FromInt64(l),
        double d => FromDouble(d),
        bool b => FromBoolean(b),
        JsonElement je => je,
        null => NullElement(),
        _ => FromString(value.ToString())
    };

    public static Dictionary<string, JsonElement> Dict(params (string Key, JsonElement Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>(pairs.Length);
        foreach (var (key, val) in pairs)
            dict[key] = val;
        return dict;
    }
}
