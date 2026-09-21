namespace JoinCode.Abstractions.Mcp.Protocol;

[JsonConverter(typeof(JsonRpcIdConverter))]
public readonly struct JsonRpcId : IEquatable<JsonRpcId> {
    private readonly object? _value;

    internal object? InternalValue => _value;

    private JsonRpcId(object? value) => _value = value;

    /// <summary>获取是否为 null。</summary>
    public bool IsNull => _value is null;
    /// <summary>获取是否为字符串。</summary>
    public bool IsString => _value is string;
    /// <summary>获取是否为数字。</summary>
    public bool IsNumber => _value is long;

    /// <summary>获取字符串值。</summary>
    public string? AsString => _value as string;
    /// <summary>获取数字值。</summary>
    public long? AsNumber => _value as long?;

    /// <summary>获取表示 null 的 JsonRpcId。</summary>
    public static JsonRpcId Null => new(null);
    /// <summary>从字符串创建 JsonRpcId。</summary>
    public static JsonRpcId FromString(string value) => new(value);
    /// <summary>从数字创建 JsonRpcId。</summary>
    public static JsonRpcId FromNumber(long value) => new(value);

    /// <summary>返回当前值的字符串表示。</summary>
    public override string ToString() => _value?.ToString() ?? "null";

    /// <summary>判断是否等于另一个 JsonRpcId。</summary>
    public bool Equals(JsonRpcId other) => Equals(_value, other._value);

    /// <summary>判断是否等于指定对象。</summary>
    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);

    /// <summary>获取哈希值。</summary>
    public override int GetHashCode() => _value?.GetHashCode() ?? 0;

    /// <summary>相等比较运算符。</summary>
    public static bool operator ==(JsonRpcId left, JsonRpcId right) => left.Equals(right);

    /// <summary>不等比较运算符。</summary>
    public static bool operator !=(JsonRpcId left, JsonRpcId right) => !left.Equals(right);

    /// <summary>从字符串隐式转换为 JsonRpcId。</summary>
    public static implicit operator JsonRpcId(string? value) => value is null ? Null : FromString(value);

    /// <summary>从 long 隐式转换为 JsonRpcId。</summary>
    public static implicit operator JsonRpcId(long value) => FromNumber(value);
}

public sealed class JsonRpcIdConverter : JsonConverter<JsonRpcId> {
    /// <summary>读取并解析 JSON 为 JsonRpcId。</summary>
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null)
            return JsonRpcId.Null;

        if (reader.TokenType == JsonTokenType.String)
            return JsonRpcId.FromString(reader.GetString()!);

        if (reader.TokenType == JsonTokenType.Number) {
            if (reader.TryGetInt64(out var longValue))
                return JsonRpcId.FromNumber(longValue);
        }

        throw new JsonException($"Invalid JSON-RPC id type: {reader.TokenType}");
    }

    /// <summary>将 JsonRpcId 写入 JSON。</summary>
    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options) {
        if (value.IsNull) {
            writer.WriteNullValue();
            return;
        }

        if (value.IsString) {
            writer.WriteStringValue(value.AsString);
            return;
        }

        if (value.IsNumber) {
            writer.WriteNumberValue(value.AsNumber.GetValueOrDefault());
            return;
        }

        throw new JsonException($"Invalid JSON-RPC id value type: {value.InternalValue?.GetType().Name}");
    }
}
