using JoinCode.Abstractions.Attributes;

namespace Core.Context;

[Register]
public sealed partial class FileSessionMetaStore : ISessionMetaStore
{
    private readonly string _directoryPath;
    private readonly IFileSystem _fs;

    /// <summary>
    /// 初始化基于文件系统的会话元数据存储，指定文件系统和存储目录路径。
    /// </summary>
    public FileSessionMetaStore(IFileSystem fs, string? directoryPath = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _directoryPath = directoryPath ?? Path.Combine(AppContext.BaseDirectory, AppDataConstants.SessionsFolderName);
    }

    private string GetFilePath(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        foreach (var c in sessionId)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                throw new ArgumentException($"Session ID contains invalid character: '{c}'", nameof(sessionId));
            }
        }

        return Path.Combine(_directoryPath, $"{sessionId}.meta.json");
    }

    /// <summary>
    /// 从文件加载指定会话的元数据，文件不存在或解析失败时返回 null。
    /// </summary>
    public async Task<SessionMeta?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(sessionId);

        if (!_fs.FileExists(filePath))
        {
            return null;
        }

        try
        {
            var json = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return SessionMetaSerializer.Deserialize(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将会话元数据序列化并写入文件，目录不存在时自动创建。
    /// </summary>
    public async Task SaveAsync(string sessionId, SessionMeta meta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meta);

        DirectoryHelper.EnsureDirectoryExists(_fs, _directoryPath);

        var filePath = GetFilePath(sessionId);
        var json = SessionMetaSerializer.Serialize(meta);
        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }
}
