namespace JoinCode.ChatCommands;

public sealed class CodeAnalysisReport
{
    public string Path { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public List<FileTypeEntry> FileTypes { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
}

public sealed class FileTypeEntry
{
    public string Extension { get; set; } = string.Empty;
    public int Count { get; set; }
}
