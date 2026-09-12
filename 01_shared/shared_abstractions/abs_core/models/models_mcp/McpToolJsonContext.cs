namespace JoinCode.Abstractions.Models;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(StepEvidence.StepEvidenceInput))]
[JsonSerializable(typeof(List<StepEvidence.StepEvidenceInput>))]
[JsonSerializable(typeof(Todo.TodoItemInput))]
[JsonSerializable(typeof(List<Todo.TodoItemInput>))]
[JsonSerializable(typeof(List<string>))]
public partial class McpToolJsonContext : JsonSerializerContext;
