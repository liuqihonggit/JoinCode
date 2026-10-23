namespace JoinCode.Gui.WebHost;

/// <summary>
/// 一轮改动的持久事件载荷（<c>workspace/changes</c>）：前端只校验 <c>turn ≥ 1</c>，
/// 收到后按事件 seq 调 <c>GET /api/changes.summary</c> 读摘要（TASK066 C7）。
/// </summary>
internal sealed class DshWorkspaceChangesData {
    /// <summary>轮次号</summary>
    public int Turn { get; set; }
}

/// <summary>
/// 一轮文件改动摘要（<c>GET /api/changes.summary</c>）：<see cref="Total"/> 是改动文件数，
/// <see cref="Added"/>/<see cref="Deleted"/> 是行数合计；文件顺序与 diff 的下标一一对应。
/// </summary>
internal sealed class DshChangesSummary {
    /// <summary>轮次号</summary>
    public int Turn { get; set; }
    /// <summary>改动文件列表（diff 下标 = 该列表下标）</summary>
    public List<DshChangedFile> Files { get; set; } = [];
    /// <summary>改动文件数（前端据此选择单文件卡片/折叠列表）</summary>
    public int Total { get; set; }
    /// <summary>新增行合计</summary>
    public int Added { get; set; }
    /// <summary>删除行合计</summary>
    public int Deleted { get; set; }
    /// <summary>每个文件的完整对比（不参与 JSON；前端按 index 单独读取）</summary>
    [JsonIgnore]
    public List<DshChangesDiff> Diffs { get; set; } = [];
}

/// <summary>改动文件摘要条目：path 为规范化绝对路径，display 为相对 cwd 的展示路径。</summary>
internal sealed class DshChangedFile {
    /// <summary>绝对路径（打开/预览用）</summary>
    public string Path { get; set; } = "";
    /// <summary>展示路径（cwd 内为相对路径）</summary>
    public string Display { get; set; } = "";
    /// <summary>新增行数</summary>
    public int Added { get; set; }
    /// <summary>删除行数</summary>
    public int Deleted { get; set; }
    /// <summary>二进制文件（前端只接受 true 或字段缺失）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Binary { get; set; }
    /// <summary>超过对比上限（前端只接受 true 或字段缺失）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Oversized { get; set; }
}

/// <summary>
/// 单文件对比（<c>GET /api/changes.diff</c>）：<c>kind=text</c> 时携带 before/after/coarse/hunks，
/// binary/oversized 只带 kind/path/display。
/// </summary>
internal sealed class DshChangesDiff {
    /// <summary>对比种类：text / binary / oversized</summary>
    public string Kind { get; set; } = "text";
    /// <summary>绝对路径</summary>
    public string Path { get; set; } = "";
    /// <summary>展示路径</summary>
    public string Display { get; set; } = "";
    /// <summary>轮次起点是否存在（text 专用）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Before { get; set; }
    /// <summary>轮次终点是否存在（text 专用）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? After { get; set; }
    /// <summary>逐行对比不可用时按整文件替换显示（text 专用）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Coarse { get; set; }
    /// <summary>对比块（binary/oversized 为空表，前端对这两种 kind 不读该字段）</summary>
    public List<DshChangesHunk> Hunks { get; set; } = [];
}

/// <summary>对比块：lines 每行以 '+' / '-' / ' ' 开头（前端 hunkRows 依此配对行号）。</summary>
internal sealed class DshChangesHunk {
    /// <summary>旧侧起始行（1 基；纯新增为 0）</summary>
    public int OldStart { get; set; }
    /// <summary>旧侧行数</summary>
    public int OldLines { get; set; }
    /// <summary>新侧起始行（1 基；纯删除为 0）</summary>
    public int NewStart { get; set; }
    /// <summary>新侧行数</summary>
    public int NewLines { get; set; }
    /// <summary>带前缀的行内容</summary>
    public List<string> Lines { get; set; } = [];
}

/// <summary>
/// 宿主桌面能力（<c>GET /api/present.host</c>）：交付卡片据此决定是否显示「打开」动作，
/// <see cref="FileManager"/> 只用前端接受的三值（finder/explorer/directory）。
/// </summary>
internal sealed class DshPresentHost {
    /// <summary>宿主名称（展示用）</summary>
    public string Name { get; set; } = "";
    /// <summary>当前环境是否能把文件交给桌面应用</summary>
    public bool Available { get; set; }
    /// <summary>文件管理器种类（null 表示无）</summary>
    public string? FileManager { get; set; }
}

/// <summary>
/// 一轮改动的内存存储（TASK066 C7）：键 =（会话, <c>workspace/changes</c> 事件序号）。
/// 与事件同生命周期，不持久化 —— 宿主重启后旧事件读不到摘要，前端按 missing 处理（DSH 契约允许）。
/// </summary>
internal sealed class DshWorkspaceChangesStore {
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<long, DshChangesSummary>> _bySession = new(StringComparer.Ordinal);

    /// <summary>记录一轮摘要（事件推送前调用，保证前端收到事件即可读到）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="seq">announcing 事件序号</param>
    /// <param name="summary">摘要（含 diffs）</param>
    internal void Record(string sessionId, long seq, DshChangesSummary summary) {
        lock (_gate) {
            if (!_bySession.TryGetValue(sessionId, out var entries)) {
                entries = [];
                _bySession[sessionId] = entries;
            }
            entries[seq] = summary;
        }
    }

    /// <summary>读一轮摘要（未知坐标返回 null → 路由回 404）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="seq">announcing 事件序号</param>
    internal DshChangesSummary? Summary(string sessionId, long seq) {
        lock (_gate) {
            return _bySession.TryGetValue(sessionId, out var entries) && entries.TryGetValue(seq, out var summary)
                ? summary
                : null;
        }
    }

    /// <summary>读单文件对比（index 越界返回 null → 路由回 404）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="seq">announcing 事件序号</param>
    /// <param name="index">摘要 files 的下标</param>
    internal DshChangesDiff? Diff(string sessionId, long seq, int index) {
        var summary = Summary(sessionId, seq);
        return summary is not null && index >= 0 && index < summary.Diffs.Count ? summary.Diffs[index] : null;
    }
}
