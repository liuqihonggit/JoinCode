
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// ModelModalityKind JSON 转换器 — [Flags] 位标志 ↔ 字符串数组互转
/// 序列化: Text | ReadImage | ToolUse → ["text","readImage","toolUse"]
/// 反序列化: ["text","readImage","toolUse"] → Text | ReadImage | ToolUse
/// </summary>
public sealed class ModelModalityKindJsonConverter : JsonConverter<ModelModalityKind>
{
    private static readonly ModelModalityKind[] SingleFlags =
    [
        ModelModalityKind.Text,
        ModelModalityKind.ReadImage,
        ModelModalityKind.ReadGif,
        ModelModalityKind.ReadVideo,
        ModelModalityKind.ReadAudio,
        ModelModalityKind.ReadPdf,
        ModelModalityKind.GenerateImage,
        ModelModalityKind.GenerateVideo,
        ModelModalityKind.GenerateAudio,
        ModelModalityKind.Thinking,
        ModelModalityKind.CodeExecution,
        ModelModalityKind.WebSearch,
        ModelModalityKind.ToolUse
    ];

    public override ModelModalityKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var flags = ModelModalityKind.None;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var str = reader.GetString();
                if (str is not null && ModelModalityKindExtensions.FromValue(str) is { } flag)
                    flags |= flag;
            }
            return flags;
        }

        if (reader.TokenType == JsonTokenType.Number)
            return (ModelModalityKind)reader.GetInt32();

        return ModelModalityKind.Text;
    }

    public override void Write(Utf8JsonWriter writer, ModelModalityKind value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var flag in SingleFlags)
        {
            if (value.HasFlag(flag))
                writer.WriteStringValue(flag.ToValue());
        }
        writer.WriteEndArray();
    }
}
