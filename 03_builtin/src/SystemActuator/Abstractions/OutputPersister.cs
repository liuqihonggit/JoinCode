namespace Services.SystemActuator;

internal static class OutputPersister
{
    internal static async Task<(string? Path, long? Size)> PersistLargeOutputAsync(
        string output, IFileSystem fs, ILogger? logger)
    {
        try
        {
            var tempDir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeToolResultsDirectory;
            DirectoryHelper.EnsureDirectoryExists(fs, tempDir);

            var filePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}"[..^20] + ".txt");
            await fs.WriteAllTextAsync(filePath, output).ConfigureAwait(false);

            var fileSize = fs.GetFileLength(filePath);
            if (fileSize > SystemActuatorExecutionResult.MaxPersistedSizeBytes)
            {
                var truncated = output[..(int)SystemActuatorExecutionResult.MaxPersistedSizeBytes];
                await fs.WriteAllTextAsync(filePath, truncated).ConfigureAwait(false);
                fileSize = SystemActuatorExecutionResult.MaxPersistedSizeBytes;
            }

            return (filePath, fileSize);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "大输出持久化失败，尝试重试一次");
            try
            {
                var retryDir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeToolResultsDirectory;
                DirectoryHelper.EnsureDirectoryExists(fs, retryDir);
                var retryPath = Path.Combine(retryDir, $"{Guid.NewGuid():N}"[..^20] + ".txt");
                await fs.WriteAllTextAsync(retryPath, output).ConfigureAwait(false);
                var retrySize = fs.GetFileLength(retryPath);
                return (retryPath, retrySize);
            }
            catch (Exception retryEx)
            {
                logger?.LogError(retryEx, "大输出持久化重试也失败，数据将丢失");
                return (null, null);
            }
        }
    }
}
