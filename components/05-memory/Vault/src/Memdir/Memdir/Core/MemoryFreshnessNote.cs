
namespace Core.Memdir;

/// <summary>
/// 记忆新鲜度提示 — 委托给 JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote
/// 对齐 TS: memdir/memoryAge.ts
/// </summary>
public static class MemoryFreshnessNote
{
    /// <inheritdoc cref="JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.MemoryAgeDays"/>
    public static int MemoryAgeDays(long mtimeMs) => JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.MemoryAgeDays(mtimeMs);

    /// <inheritdoc cref="JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.MemoryAge"/>
    public static string MemoryAge(long mtimeMs) => JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.MemoryAge(mtimeMs);

    /// <inheritdoc cref="JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.FreshnessText"/>
    public static string FreshnessText(long mtimeMs) => JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.FreshnessText(mtimeMs);

    /// <inheritdoc cref="JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.FreshnessNote"/>
    public static string FreshnessNote(long mtimeMs) => JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.FreshnessNote(mtimeMs);

    /// <inheritdoc cref="JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.IsMemoryFile"/>
    public static bool IsMemoryFile(string filePath) => JoinCode.Abstractions.Configuration.Settings.MemoryFreshnessNote.IsMemoryFile(filePath);
}
