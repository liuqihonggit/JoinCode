namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 输出 JSON 序列化上下文 — AOT 兼容
/// <para>泛型信封 CliOutputEnvelope&lt;T&gt; 必须在此注册每个 T 组合，
/// 否则 ToJsonString 运行时报 JsonTypeInfo not found。</para>
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<System.Collections.Generic.List<CliToolListItem>>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<System.Collections.Generic.List<CliToolSearchItem>>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<System.Collections.Generic.List<CliSlashCommandListItem>>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<CliNonInteractiveResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<CliSchemaResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<JoinCode.CliCommands.RgJsonResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<JoinCode.CliCommands.McpServeExitReport>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolSchema>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputEnvelope<CliSlashSchemaHintResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStructuredError))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliOutputMeta))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<CliOutputEnvelope>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStreamEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliStreamEventData))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<CliToolListItem>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<CliToolSearchItem>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<CliSlashCommandListItem>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.Abstractions.Tools.ToolResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliNonInteractiveResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliSchemaResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliSchemaPropertyDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliSchemaPropertyDto[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.CliCommands.RgJsonResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.CliCommands.RgJsonMatch))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<JoinCode.CliCommands.RgJsonMatch>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.CliCommands.McpServeExitReport))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.Abstractions.Tools.ToolSchema))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JoinCode.Abstractions.Tools.ToolSchemaProperty))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, JoinCode.Abstractions.Tools.ToolSchemaProperty>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CliSlashSchemaHintResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CrashDumpSnapshotDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CrashDumpFrameDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CrashDumpContextDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CrashDumpFrameDto[]))]
public partial class CliOutputJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
