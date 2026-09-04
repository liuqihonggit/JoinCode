namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 输出 JSON 序列化上下文 — AOT 兼容
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStructuredError))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputMeta))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<CliOutputEnvelope>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStreamEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStreamEventData))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<CliToolListItem>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<CliToolSearchItem>))]
public partial class CliOutputJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
