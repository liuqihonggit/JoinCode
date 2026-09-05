
namespace Services.Todo;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<TodoItemInput>))]
[JsonSerializable(typeof(List<TodoItem>))]
public partial class TodoJsonContext : JsonSerializerContext;
