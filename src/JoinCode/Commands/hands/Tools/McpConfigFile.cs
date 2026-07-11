namespace JoinCode.ChatCommands;

public sealed class McpServerConfigEntry
{
    public string Type { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public sealed class McpConfigFile
{
    public Dictionary<string, McpServerConfigEntry> McpServers { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(McpConfigFile))]
[JsonSerializable(typeof(McpServerConfigEntry))]
[JsonSerializable(typeof(Dictionary<string, McpServerConfigEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
public partial class McpConfigJsonContext : JsonSerializerContext;
