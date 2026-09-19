
namespace McpToolDispatch;

/// <summary>
/// MCP 工具派发 JSON 序列化上下文 — 为 AOT 编译预生成 string、List&lt;string&gt;、Dictionary&lt;string, JsonElement&gt; 的序列化代码
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class McpToolDispatchJsonContext : JsonSerializerContext;