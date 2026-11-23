namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 已持久化的文件信息 — 承载 system/files_persisted 事件的单条成功记录。
/// </summary>
/// <param name="Filename">落盘文件名。</param>
/// <param name="FileId">文件标识；无标识时为空串。</param>
public sealed record PersistedFileInfo(string Filename, string FileId);

/// <summary>
/// 持久化失败的文件信息 — 承载 system/files_persisted 事件的单条失败记录。
/// </summary>
/// <param name="Filename">落盘文件名。</param>
/// <param name="Error">失败原因。</param>
public sealed record FailedPersistedFileInfo(string Filename, string Error);
