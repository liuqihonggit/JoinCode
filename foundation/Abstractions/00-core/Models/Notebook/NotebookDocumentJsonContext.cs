
namespace JoinCode.Abstractions.Models.Notebook;

/// <summary>
/// Notebook 文档 JSON 序列化上下文（AOT 兼容）
/// 对齐 TS: notebook.ts 的 JSON 解析
/// </summary>
[JsonSerializable(typeof(NotebookDocument))]
[JsonSerializable(typeof(NotebookCell))]
[JsonSerializable(typeof(NotebookOutput))]
[JsonSerializable(typeof(NotebookMetadata))]
[JsonSerializable(typeof(List<NotebookCell>))]
[JsonSerializable(typeof(List<NotebookOutput>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class NotebookDocumentJsonContext : JsonSerializerContext;
