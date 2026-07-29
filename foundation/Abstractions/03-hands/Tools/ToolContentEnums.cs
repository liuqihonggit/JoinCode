
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具内容类型 — 替代 ToolContent.Type 的字符串常量
/// </summary>
public enum ToolContentType
{
    [EnumValue("text")] Text = 0,
    [EnumValue("image")] Image = 1,
    [EnumValue("resource")] Resource = 2,
    [EnumValue("error")] Error = 3,
    [EnumValue("document")] Document = 4
}

/// <summary>
/// ToolContentType JSON 转换器 — 保持协议兼容（序列化为小写字符串，反序列化从字符串解析）
/// </summary>
public sealed class ToolContentTypeJsonConverter : JsonConverter<ToolContentType>
{
    public override ToolContentType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "text" => ToolContentType.Text,
            "image" => ToolContentType.Image,
            "resource" => ToolContentType.Resource,
            "error" => ToolContentType.Error,
            "document" => ToolContentType.Document,
            _ => ToolContentType.Text
        };
    }

    public override void Write(Utf8JsonWriter writer, ToolContentType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToValue());
    }
}
