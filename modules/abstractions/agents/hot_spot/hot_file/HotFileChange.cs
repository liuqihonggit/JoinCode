namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热文件变更记录 — 被修改的文件路径与修改者标识
/// </summary>
/// <param name="FilePath">被修改的文件路径</param>
/// <param name="ChangerId">修改者标识(worker id 或 captain)</param>
public sealed record HotFileChange(string FilePath, string ChangerId);
