namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 后台家政清理服务接口 — 对齐 TS startBackgroundHousekeeping / cleanupOldMessageFilesInBackground
/// 聚合调度所有 CleanupOld* 方法，延迟执行+循环清理
/// </summary>
public interface IHousekeepingService
{
    /// <summary>
    /// 执行全部清理操作 — 对齐 TS cleanupOldMessageFilesInBackground
    /// 依次调用: CleanupOldSessionFiles, CleanupOldFileHistoryBackups,
    /// CleanupOldSessionEnvDirs, CleanupOldDebugLogs, CleanupOldMessageFiles,
    /// CleanupOldImageCaches, CleanupOldPastes, CleanupNpmCache, CleanupOldVersions
    /// </summary>
    /// <param name="currentSessionId">当前会话 ID，CleanupOldImageCaches 会跳过此会话的目录；空字符串则全部删除</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<int> RunAllCleanupAsync(string currentSessionId = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理旧会话文件 — 对齐 TS cleanupOldSessionFiles
    /// 删除 sessions/*.jsonl + *.cast + tool-results/ 中 mtime 超过指定天数的
    /// </summary>
    int CleanupOldSessionFiles(int maxAgeDays = 30);

    /// <summary>
    /// 清理旧文件历史备份 — 对齐 TS cleanupOldFileHistoryBackups
    /// 删除 file-history/ 中 mtime 超过指定天数的子目录
    /// </summary>
    int CleanupOldFileHistoryBackups(int maxAgeDays = 30);

    /// <summary>
    /// 清理旧会话环境目录 — 对齐 TS cleanupOldSessionEnvDirs
    /// 删除 session-env/ 中 mtime 超过指定天数的子目录
    /// </summary>
    int CleanupOldSessionEnvDirs(int maxAgeDays = 30);

    /// <summary>
    /// 清理旧调试日志 — 对齐 TS cleanupOldDebugLogs
    /// 删除 debug/*.txt 中 mtime 超过指定天数的文件
    /// </summary>
    int CleanupOldDebugLogs(int maxAgeDays = 30);

    /// <summary>
    /// 清理旧消息/错误日志 — 对齐 TS cleanupOldMessageFiles
    /// 删除 errors/ + mcp-logs-* 中 mtime 超过指定天数的文件
    /// </summary>
    int CleanupOldMessageFiles(int maxAgeDays = 30);

    /// <summary>
    /// 清理旧 npm 缓存 — 对齐 TS cleanupNpmCacheForAnthropicPackages
    /// 删除 ~/.npm/_cacache 中 @anthropic-ai/claude-* 缓存条目
    /// </summary>
    int CleanupNpmCache(int maxAgeDays = 1, int retentionCount = 5);

    /// <summary>
    /// 清理旧版本二进制 — 对齐 TS cleanupOldVersions
    /// 删除 jcc.exe.old.* + 孤立 staging/versions 临时文件
    /// </summary>
    int CleanupOldVersions(int retentionCount = 2);

    /// <summary>
    /// 清理旧图片缓存目录 — 对齐 TS cleanupOldImageCaches
    /// 删除 image-cache/ 下非当前会话的子目录，空目录也删除
    /// </summary>
    int CleanupOldImageCaches(string currentSessionId);

    /// <summary>
    /// 清理旧粘贴缓存 — 对齐 TS cleanupOldPastes
    /// 删除 paste-cache/ 中 mtime 超过指定天数的 .txt 文件
    /// </summary>
    int CleanupOldPastes(int maxAgeDays = 30);
}
