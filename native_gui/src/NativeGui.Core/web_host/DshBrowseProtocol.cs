namespace JoinCode.Gui.WebHost;

// 目录浏览与 @ 文件引用 DTO（TASK066 C6）。字段形状对齐 dsh-api-workspace-controller#directoryPicker/*
// 与 dsh-api-session-controller#fileReferences/list 的生成器 schema。

/// <summary>directoryPicker/list 参数（path 可省略，省略时从主目录开始）</summary>
internal sealed class DshDirectoryListArgs {
    /// <summary>目标目录绝对路径</summary>
    public string? Path { get; set; }
}

/// <summary>directoryPicker/createDirectory 参数（平铺 wire：path + name）</summary>
internal sealed class DshDirectoryCreateArgs {
    /// <summary>父目录绝对路径</summary>
    public string Path { get; set; } = "";
    /// <summary>单段目录名</summary>
    public string Name { get; set; } = "";
}

/// <summary>目录浏览结果（只含子目录；隐藏项保留并打标）</summary>
internal sealed class DshDirectoryListing {
    /// <summary>当前目录绝对路径</summary>
    public string Path { get; set; } = "";
    /// <summary>主目录绝对路径</summary>
    public string Home { get; set; } = "";
    /// <summary>从根到当前目录的面包屑</summary>
    public List<DshDirectoryCrumb> Crumbs { get; set; } = [];
    /// <summary>子目录条目（按名称升序，最多 1000 条）</summary>
    public List<DshDirectoryEntry> Entries { get; set; } = [];
    /// <summary>条目是否被上限截断</summary>
    public bool Truncated { get; set; }
}

/// <summary>目录面包屑</summary>
internal sealed class DshDirectoryCrumb {
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
    /// <summary>跳转路径</summary>
    public string Path { get; set; } = "";
    /// <summary>是否隐藏项</summary>
    public bool Hidden { get; set; }
}

/// <summary>目录条目</summary>
internal sealed class DshDirectoryEntry {
    /// <summary>目录名</summary>
    public string Name { get; set; } = "";
    /// <summary>目录绝对路径</summary>
    public string Path { get; set; } = "";
    /// <summary>是否隐藏项（名称以 . 开头）</summary>
    public bool Hidden { get; set; }
}

/// <summary>fileReferences/list 参数（平铺 wire：agentId + query）</summary>
internal sealed class DshFileReferencesListArgs {
    /// <summary>会话 ID（解析工作目录根）</summary>
    public string AgentId { get; set; } = "";
    /// <summary>@ 后面的查询文本</summary>
    public string? Query { get; set; } = "";
}

/// <summary>@ 文件引用候选</summary>
internal sealed class DshFileReferenceItem {
    /// <summary>相对工作目录的路径（正斜杠）</summary>
    public string Path { get; set; } = "";
    /// <summary>file / directory</summary>
    public string Kind { get; set; } = "file";
}

/// <summary>目录浏览失败（code 直接作为 RPC 错误码）</summary>
internal sealed class DshDirectoryBrowserException(string code, string message) : Exception(message) {
    /// <summary>稳定错误码</summary>
    internal string Code { get; } = code;
}

/// <summary>文件引用补全失败（code 直接作为 RPC 错误码）</summary>
internal sealed class DshFileReferenceException(string code, string message) : Exception(message) {
    /// <summary>稳定错误码</summary>
    internal string Code { get; } = code;
}
