namespace JoinCode.Gui.Hosting;

/// <summary>可浏览的文件系统条目。</summary>
public sealed record WorkspaceFile(string Name, string Path, bool IsDirectory) {
    /// <summary>文件夹和文件的显示标识。</summary>
    public string Display => $"{(IsDirectory ? "▸" : "·")} {Name}";
}
