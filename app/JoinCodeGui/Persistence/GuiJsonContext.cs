namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化 JSON 上下文 — AOT 兼容（源码生成），
/// 序列化选项对齐 CLI CliIndentedJsonContext：PascalCase 默认命名 + WriteIndented。
/// </summary>
[JsonSourceGenerationOptions(
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
