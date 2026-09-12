namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化 JSON 上下文 — AOT 兼容（源码生成），
/// camelCase 命名 + WriteIndented + 真实中文输出（通过 RelaxedJsonSerializer）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GuiSessionData))]
[JsonSerializable(typeof(GuiSessionMessage))]
[JsonSerializable(typeof(List<GuiSessionMessage>))]
[JsonSerializable(typeof(GuiSessionSummary))]
[JsonSerializable(typeof(List<GuiSessionSummary>))]
[JsonSerializable(typeof(GuiPreferences))]
public partial class GuiJsonContext : JsonSerializerContext;
