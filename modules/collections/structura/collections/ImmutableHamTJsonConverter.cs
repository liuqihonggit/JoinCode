// JCC11003 抑制: 底层泛型集合实现, ! 用于 default(T)/数组槽位的可空抑制, 是 C# 语言限制下的必要用法
// JCC11005 抑制: JsonConverter.Read 是框架接口实现, 必须返回 T?, 不能改 TryGet 模式
#pragma warning disable JCC11003, JCC11005
namespace Structura.Collections;

/// <summary>ImmutableHamT&lt;string, string&gt; 的 JSON 转换器 — 将 HAMT 序列化为 JSON 对象,反序列化时重建 HAMT。用于 AOT 场景下替代 BCL ImmutableDictionary 的内置转换器。</summary>
public sealed class ImmutableHamTStringStringConverter : JsonConverter<ImmutableHamT<string, string>> {
    /// <summary>读取 JSON 对象并重建 ImmutableHamT。</summary>
    public override ImmutableHamT<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject for ImmutableHamT<string,string>");
        var result = ImmutableHamT.Create<string, string>();
        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            var key = reader.GetString()!;
            reader.Read();
            var value = reader.GetString()!;
            result = result.Add(key, value);
        }
        return result;
    }

    /// <summary>将 ImmutableHamT 写入为 JSON 对象。</summary>
    public override void Write(Utf8JsonWriter writer, ImmutableHamT<string, string> value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        foreach (var kv in value)
            writer.WriteString(kv.Key, kv.Value);
        writer.WriteEndObject();
    }
}
