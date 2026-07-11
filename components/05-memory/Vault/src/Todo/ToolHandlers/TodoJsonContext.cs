
namespace Services.Todo;

[JsonSerializable(typeof(List<TodoItemInput>))]
public partial class TodoJsonContext : JsonSerializerContext;
