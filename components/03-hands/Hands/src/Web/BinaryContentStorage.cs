namespace Services.Web;

/// <summary>
/// 二进制内容持久化 — 对齐TS版 mcpOutputStorage.ts 的 persistBinaryContent
/// 将二进制响应（PDF、图片等）的原始字节保存到 {sessionDir}/tool-results/{persistId}.{ext}
/// </summary>
[Register(typeof(IBinaryContentStorage))]
public sealed partial class BinaryContentStorage : IBinaryContentStorage
{
    [Inject] private readonly ILogger<BinaryContentStorage>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly IFileSystem _fs;

    public BinaryContentStorage(IFileSystem fs, ILogger<BinaryContentStorage>? logger = null, IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 持久化二进制内容到文件
    /// 对齐TS版: persistBinaryContent(bytes, mimeType, persistId)
    /// </summary>
    /// <param name="bytes">原始字节</param>
    /// <param name="mimeType">MIME类型</param>
    /// <param name="persistId">持久化ID，格式: webfetch-{timestamp}-{random}</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>持久化结果（路径+大小+扩展名，或错误信息）</returns>
    public async Task<BinaryPersistResult> PersistAsync(
        byte[] bytes,
        string? mimeType,
        string persistId,
        CancellationToken cancellationToken = default)
    {
        var ext = MimeTypeExtensionMapper.GetExtension(mimeType);
        var directory = GetToolResultsDirectory();
        var fileName = $"{persistId}.{ext}";
        var filePath = Path.Combine(directory, fileName);

        try
        {
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);
            await _fs.WriteAllBytesAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("二进制内容已持久化: {Path}, Size={Size}, Ext={Ext}", filePath, bytes.Length, ext);

            return new BinaryPersistResult
            {
                FilePath = filePath,
                Size = bytes.Length,
                Extension = ext
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "二进制内容持久化失败: {Path}", filePath);
            return new BinaryPersistResult
            {
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// 生成持久化ID — 对齐TS版: webfetch-{timestamp}-{random6chars}
    /// </summary>
    public string GeneratePersistId()
    {
        var timestamp = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
        var random = Random.Shared.Next(0, 0x1000000).ToString("x6")[..6];
        return $"webfetch-{timestamp}-{random}";
    }

    /// <summary>
    /// 获取工具结果目录 — 对齐TS版 getToolResultsDir
    /// 路径: {cwd}/.jcc/sessions/tool-results/
    /// </summary>
    private string GetToolResultsDirectory()
    {
        var cwd = _fs.GetCurrentDirectory();
        var appData = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        return Path.Combine(appData, AppDataConstants.SessionsFolderName, AppDataConstants.ToolResultsFolderName);
    }
}
