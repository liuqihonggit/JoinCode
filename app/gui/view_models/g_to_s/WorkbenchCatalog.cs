namespace JoinCode.Gui.ViewModels;

/// <summary>从引擎目录派生工具分组和参数表单。</summary>
internal static class WorkbenchCatalog {
    /// <summary>按工具命名约定归类，不维护工具副本。</summary>
    internal static string Group(string name) => name switch {
        var n when n.StartsWith("file_", StringComparison.Ordinal) => "文件",
        var n when n.StartsWith("git_", StringComparison.Ordinal) => "Git",
        var n when n.StartsWith("mcp_", StringComparison.Ordinal) => "MCP",
        var n when n.Contains("plugin", StringComparison.OrdinalIgnoreCase) => "插件",
        _ => "其他"
    };

    /// <summary>严格解析对象参数，非法 JSON 不退回空参数执行。</summary>
    internal static Dictionary<string, JsonElement> ParseArguments(string json) {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("参数必须是 JSON 对象，例如 {\"path\":\"src/app.cs\"}。");
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}

/// <summary>引擎 schema 驱动的可编辑参数。</summary>
public sealed partial class WorkbenchParameter : ObservableObject {
    /// <summary>参数名。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>类型及必填提示。</summary>
    public string Hint { get; init; } = string.Empty;
    /// <summary>原始 schema。</summary>
    internal ToolSchemaProperty Schema { get; init; } = new();
    /// <summary>参数输入；复杂类型使用 JSON。</summary>
    [ObservableProperty] private string _value = string.Empty;
}

/// <summary>可浏览的文件系统条目。</summary>
public sealed record WorkspaceFile(string Name, string Path, bool IsDirectory) {
    /// <summary>文件夹和文件的显示标识。</summary>
    public string Display => $"{(IsDirectory ? "▸" : "·")} {Name}";
}
