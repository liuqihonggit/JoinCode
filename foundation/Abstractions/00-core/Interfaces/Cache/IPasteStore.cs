namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 粘贴内容缓存接口 — 对齐 TS pasteStore.ts
/// 内容寻址持久化缓存：大文本超过阈值时不在历史中内联存储，而是存到 paste-cache 目录
/// </summary>
public interface IPasteStore : IStore
{
    /// <summary>
    /// 计算粘贴文本的内容哈希 — 对齐 TS hashPastedText
    /// SHA-256 前 16 位十六进制，用作文件名
    /// </summary>
    string HashPastedText(string content);

    /// <summary>
    /// 将粘贴文本持久化到磁盘 — 对齐 TS storePastedText
    /// 内容寻址：相同哈希 = 相同内容，覆盖写入是安全的
    /// </summary>
    void StorePastedText(string hash, string content);

    /// <summary>
    /// 从磁盘读取粘贴文本 — 对齐 TS retrievePastedText
    /// 不存在时返回 null
    /// </summary>
    string? RetrievePastedText(string hash);
}
