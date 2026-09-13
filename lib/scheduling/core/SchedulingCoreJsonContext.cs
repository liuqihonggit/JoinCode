namespace Core.Scheduling;

/// <summary>
/// 调度核心 JSON 序列化上下文 — 紧凑格式（不缩进），覆盖任务元数据与基础标量类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FileTaskMetadata))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class SchedulingJsonContext : JsonSerializerContext;

/// <summary>
/// 调度核心 JSON 序列化上下文 — 缩进格式，覆盖 Cron 任务文件与任务元数据
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CronTaskFile))]
[JsonSerializable(typeof(FileTaskMetadata))]
public partial class SchedulingIndentedJsonContext : JsonSerializerContext;
