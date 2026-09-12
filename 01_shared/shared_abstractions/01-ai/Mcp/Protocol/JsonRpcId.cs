namespace JoinCode.Abstractions.Mcp.Protocol;

[JsonConverter(typeof(JsonRpcIdConverter))]
public readonly struct JsonRpcId : IEquatable<JsonRpcId>
{
    private readonly object? _value;

    internal object? InternalValue => _value;

    private JsonRpcId(object? value) => _value = value;

    public bool IsNull => _value is null;
    public bool IsString => _value is string;
    public bool IsNumber => _value is long;

    public string? AsString => _value as string;
    public long? AsNumber => _value as long?;

    public static JsonRpcId Null => new(null);
    public static JsonRpcId FromString(string value) => new(value);
    public static JsonRpcId FromNumber(long value) => new(value);

    public override string ToString() => _value?.ToString() ?? "null";

    public bool Equals(JsonRpcId other) => Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);

    public override int GetHashCode() => _value?.GetHashCode() ?? 0;

    public static bool operator ==(JsonRpcId left, JsonRpcId right) => left.Equals(right);

    public static bool operator !=(JsonRpcId left, JsonRpcId right) => !left.Equals(right);

    public static implicit operator JsonRpcId(string? value) => value is null ? Null : FromString(value);

    public static implicit operator JsonRpcId(long value) => FromNumber(value);
}

public sealed class JsonRpcIdConverter : JsonConverter<JsonRpcId>
{
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return JsonRpcId.Null;

        if (reader.TokenType == JsonTokenType.String)
            return JsonRpcId.FromString(reader.GetString()!);

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long longValue))
                return JsonRpcId.FromNumber(longValue);
        }

        throw new JsonException($"Invalid JSON-RPC id type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options)
    {
        if (value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.IsString)
        {
            writer.WriteStringValue(value.AsString);
            return;
        }

        if (value.IsNumber)
        {
            writer.WriteNumberValue(value.AsNumber.GetValueOrDefault());
            return;
        }

        throw new JsonException($"Invalid JSON-RPC id value type: {value.InternalValue?.GetType().Name}");
    }
}
