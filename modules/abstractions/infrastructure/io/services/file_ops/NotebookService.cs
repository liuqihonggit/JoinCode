namespace Infrastructure.IO;

/// <summary>
/// 上下文存档服务实现（ADR 0156 P4）— 折叠/压缩时自动存档原始内容到磁盘
/// <para>存储路径：{AppDataFolder}/notebook/{sessionId}/{foldId}.json</para>
/// <para>JSON 格式包含：foldId、timestamp、messageRange、messages、summary</para>
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IContextArchiveService), ServiceLifetime.Singleton)]
public sealed partial class ContextArchiveService : ServiceEntity, JoinCode.Abstractions.LLM.Chat.IContextArchiveService {
    private readonly IFileSystem _fs;
    private readonly ILogger<ContextArchiveService>? _logger;
    private readonly string _baseDir;

    /// <summary>
    /// 构造上下文存档服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public ContextArchiveService(IFileSystem fs, ILogger<ContextArchiveService>? logger = null) {
        _fs = fs;
        _logger = logger;
        _baseDir = Path.Combine(
            AppDataConstants.AppDataFolder,
            "notebook");
    }

    /// <inheritdoc />
    public async Task<string> SaveFoldArchiveAsync(
        string sessionId,
        IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ApiMessage> foldableMessages,
        string summary,
        CancellationToken cancellationToken = default) {
        var foldId = GenerateFoldId();
        var dir = Path.Combine(_baseDir, sessionId);
        _fs.CreateDirectory(dir);

        var filepath = Path.Combine(dir, foldId + ".json");

        var archive = new FoldArchiveJson {
            FoldId = foldId,
            Timestamp = DateTimeOffset.UtcNow,
            MessageCount = foldableMessages.Count,
            Summary = summary,
            Messages = foldableMessages.Select(m => new MessageJson {
                Role = m.Role.ToString(),
                Content = m.Content ?? string.Empty
            }).ToList()
        };

        var json = JsonSerializer.Serialize(archive, FoldArchiveJsonContext.Default.FoldArchiveJson);

        try {
            await using var stream = _fs.CreateStream(filepath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        } catch (IOException ex) when (_fs.FileExists(filepath)) {
            _logger?.LogDebug(ex, "Archive already exists, skipping: {Filepath}", filepath);
        }

        _logger?.LogDebug("Saved fold archive {FoldId} with {Count} messages to {Filepath}", foldId, foldableMessages.Count, filepath);

        return foldId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry>> ListArchivesAsync(
        string sessionId,
        CancellationToken cancellationToken = default) {
        var dir = Path.Combine(_baseDir, sessionId);
        if (!_fs.DirectoryExists(dir)) {
            return [];
        }

        var entries = new List<JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry>();
        foreach (var filepath in _fs.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)) {
            try {
                var json = await _fs.ReadAllText(filepath).ConfigureAwait(false);
                var archive = JsonSerializer.Deserialize(json, FoldArchiveJsonContext.Default.FoldArchiveJson);
                if (archive is not null) {
                    entries.Add(new JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry {
                        FoldId = archive.FoldId,
                        SummaryPreview = archive.Summary.Length > 200 ? archive.Summary[..200] : archive.Summary,
                        Timestamp = archive.Timestamp,
                        MessageCount = archive.MessageCount
                    });
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to read archive: {Filepath}", filepath);
            }
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> ReadArchiveAsync(
        string sessionId,
        string foldId,
        CancellationToken cancellationToken = default) {
        var filepath = Path.Combine(_baseDir, sessionId, foldId + ".json");
        if (!_fs.FileExists(filepath)) {
            return null;
        }

        try {
            return await _fs.ReadAllText(filepath).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to read archive: {Filepath}", filepath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry>> SearchArchivesAsync(
        string sessionId,
        string keyword,
        CancellationToken cancellationToken = default) {
        var dir = Path.Combine(_baseDir, sessionId);
        if (!_fs.DirectoryExists(dir)) {
            return [];
        }

        var entries = new List<JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry>();
        foreach (var filepath in _fs.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)) {
            try {
                var json = await _fs.ReadAllText(filepath).ConfigureAwait(false);
                var archive = JsonSerializer.Deserialize(json, FoldArchiveJsonContext.Default.FoldArchiveJson);
                if (archive is not null && archive.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                    entries.Add(new JoinCode.Abstractions.LLM.Chat.ContextArchiveEntry {
                        FoldId = archive.FoldId,
                        SummaryPreview = archive.Summary.Length > 200 ? archive.Summary[..200] : archive.Summary,
                        Timestamp = archive.Timestamp,
                        MessageCount = archive.MessageCount
                    });
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to search archive: {Filepath}", filepath);
            }
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    private static string GenerateFoldId() {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var shortHash = Guid.NewGuid().ToString("N")[..8];
        return $"fold-{timestamp}-{shortHash}";
    }
}

/// <summary>折叠存档 JSON DTO</summary>
internal sealed class FoldArchiveJson {
    /// <summary>获取或设置存档唯一标识。</summary>
    public string FoldId { get; set; } = string.Empty;
    /// <summary>获取或设置存档时间。</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>获取或设置被折叠的消息数。</summary>
    public int MessageCount { get; set; }
    /// <summary>获取或设置摘要文本。</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>获取或设置被折叠的原始消息列表。</summary>
    public List<MessageJson> Messages { get; set; } = [];
}

/// <summary>消息 JSON DTO</summary>
internal sealed class MessageJson {
    /// <summary>获取或设置消息角色。</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>获取或设置消息内容。</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>折叠存档 JsonContext（AOT 兼容）</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FoldArchiveJson))]
internal sealed partial class FoldArchiveJsonContext : JsonSerializerContext {
}
