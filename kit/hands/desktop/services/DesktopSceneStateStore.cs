namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面场景状态存储实现 — JSON 文件持久化，跨 mcp_call 进程通过文件中转
/// </summary>
internal sealed class DesktopSceneStateStore : IDesktopSceneStateStore
{
    private readonly string _storeDirectory;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// 初始化状态存储
    /// </summary>
    /// <param name="storeDirectory">状态文件存储目录（如 ~/.jcc/scenarios/）</param>
    /// <param name="fileSystem">文件系统抽象</param>
    public DesktopSceneStateStore(string storeDirectory, IFileSystem fileSystem)
    {
        _storeDirectory = storeDirectory ?? throw new ArgumentNullException(nameof(storeDirectory));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>加载场景状态 — 跨进程文件中转读取</summary>
    public async Task<DesktopSceneState?> LoadAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        var path = _fileSystem.CombinePath(_storeDirectory, sceneId + ".json");
        if (!_fileSystem.FileExists(path)) return null;
        var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, DesktopSceneStateJsonContext.Default.DesktopSceneState);
    }

    /// <summary>保存场景状态 — 写文件供下次调用读取</summary>
    public async Task SaveAsync(DesktopSceneState state, CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.DirectoryExists(_storeDirectory))
            _fileSystem.CreateDirectory(_storeDirectory);
        var path = _fileSystem.CombinePath(_storeDirectory, state.SceneId + ".json");
        var json = JsonSerializer.Serialize(state, DesktopSceneStateJsonContext.Default.DesktopSceneState);
        await _fileSystem.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 桌面场景状态 JSON 序列化上下文 — AOT 兼容
/// </summary>
[JsonSerializable(typeof(DesktopSceneState))]
[JsonSerializable(typeof(ZoomHistoryEntry))]
internal sealed partial class DesktopSceneStateJsonContext : JsonSerializerContext { }
