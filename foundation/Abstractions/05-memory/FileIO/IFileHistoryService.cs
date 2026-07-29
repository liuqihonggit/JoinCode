namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Tracks file edit history for backup/restore. Mirrors TS fileHistory.
/// Before each file write/edit, a backup of the original content is saved.
/// </summary>
public interface IFileHistoryService
{
    /// <summary>
    /// Create a backup of the file before it gets overwritten.
    /// If the file doesn't exist, no backup is created (returns null).
    /// Thread-safe and idempotent for the same version.
    /// </summary>
    /// <returns>The backup file path, or null if no backup was needed.</returns>
    Task<string?> BackupBeforeWriteAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the backup path for a specific file version.
    /// Returns null if no backup exists for that version.
    /// </summary>
    string? GetBackupPath(string filePath, int version);

    /// <summary>
    /// Get all available backup versions for a file.
    /// </summary>
    IReadOnlyList<int> GetBackupVersions(string filePath);

    /// <summary>
    /// Restore a file from a backup version.
    /// </summary>
    Task<bool> RestoreAsync(string filePath, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up old backups for a specific file, keeping only the latest N versions.
    /// </summary>
    Task CleanupAsync(string filePath, int keepVersions = 5, CancellationToken cancellationToken = default);
}
