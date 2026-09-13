
namespace Core.Configuration.Remote;

/// <summary>
/// 远程设置 JSON 序列化上下文 — 为远程托管设置相关类型生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ManagedSetting))]
[JsonSerializable(typeof(List<ManagedSetting>))]
[JsonSerializable(typeof(RemoteSettingsResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class RemoteSettingsJsonContext : JsonSerializerContext;

/// <summary>
/// 远程设置响应 — 从远程端点拉取的托管设置集合
/// </summary>
public sealed class RemoteSettingsResponse
{
    /// <summary>
    /// 托管设置列表
    /// </summary>
    public List<ManagedSetting> Settings { get; set; } = [];
    /// <summary>
    /// 拉取时间戳
    /// </summary>
    public DateTime? FetchedAt { get; set; }
}
