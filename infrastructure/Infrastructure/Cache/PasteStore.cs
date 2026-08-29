namespace Infrastructure.Cache;

/// <summary>
/// 粘贴内容缓存实现 — 对齐 TS pasteStore.ts
/// 内容寻址持久化缓存：SHA-256 前 16 位作为文件名，存储在 paste-cache/ 目录
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IPasteStore), ServiceLifetime.Singleton)]
public sealed partial class PasteStore : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.IPasteStore
{

    public PasteStore(IFileSystem fs, ILogger<PasteStore>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }
    private readonly IFileSystem _fs;
    private readonly ILogger<PasteStore>? _logger;

    private static readonly string PasteCacheDir = Path.Combine(
        WorkflowConstants.Paths.JccDirectory, "paste-cache");

    /// <summary>
    /// 计算粘贴文本的内容哈希 — 对齐 TS hashPastedText
    /// SHA-256 前 16 位十六进制，用作文件名
    /// </summary>
    public string HashPastedText(string content)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes).AsSpan(0, 16).ToString();
    }

    /// <summary>
    /// 将粘贴文本持久化到磁盘 — 对齐 TS storePastedText
    /// 内容寻址：相同哈希 = 相同内容，覆盖写入是安全的
    /// </summary>
    public void StorePastedText(string hash, string content)
    {
        try
        {
            if (!_fs.DirectoryExists(PasteCacheDir))
            {
                _fs.CreateDirectory(PasteCacheDir);
            }

            var pastePath = GetPastePath(hash);
            _fs.WriteAllText(pastePath, content);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "存储粘贴内容失败: {Hash}", hash);
        }
    }

    /// <summary>
    /// 从磁盘读取粘贴文本 — 对齐 TS retrievePastedText
    /// 不存在时返回 null
    /// </summary>
    public string? RetrievePastedText(string hash)
    {
        try
        {
            var pastePath = GetPastePath(hash);
            if (!_fs.FileExists(pastePath)) return null;
            return _fs.ReadAllText(pastePath);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "读取粘贴内容失败: {Hash}", hash);
            return null;
        }
    }

    private static string GetPastePath(string hash) => Path.Combine(PasteCacheDir, $"{hash}.txt");
}
