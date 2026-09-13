
namespace Services.Todo;

/// <summary>
/// Todo 模块的 JSON 序列化上下文 — 为 AOT 编译预生成 TodoItemInput/TodoItem 列表的元数据。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<TodoItemInput>))]
[JsonSerializable(typeof(List<TodoItem>))]
public partial class TodoJsonContext : JsonSerializerContext;
