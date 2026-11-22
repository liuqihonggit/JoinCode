namespace Core.Configuration;

/// <summary>
/// 脏读重试辅助 — mmap + FileShare.ReadWrite 允许并发读写，脏读时指数退避重试16次
/// <para>16次源自二进制指数退避（以太网冲突解决），1,2,4,8...ms 上限1024ms</para>
/// <para>对 JsonException（脏读特征）和 IOException（文件锁竞争）重试，其他异常不重试</para>
/// </summary>
internal static class DirtyReadRetry {
    /// <summary>
    /// 读取并解析 — 并发写导致 JsonException 时指数退避重试16次
    /// </summary>
    /// <param name="readAsync">文件读取函数（返回文件内容）</param>
    /// <param name="parse">JSON 解析函数（返回解析结果或抛 JsonException）</param>
    /// <param name="path">文件路径（用于日志）</param>
    /// <param name="logger">日志器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<T?> ReadWithRetryAsync<T>(
        Func<Task<string>> readAsync,
        Func<string, T?> parse,
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) {
        for (var attempt = 0; attempt < 16; attempt++) {
            try {
                return parse(await readAsync().ConfigureAwait(false));
            } catch (Exception ex) when (ex is JsonException or IOException) {
                await Task.Delay(Math.Min(1 << attempt, 1024), cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                logger?.LogWarning(ex, "配置文件读取失败: {Path}", path);
                return default;
            }
        }
        logger?.LogWarning("配置文件解析失败（16次重试后仍脏读），使用默认值: {Path}", path);
        return default;
    }
}
