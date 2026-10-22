namespace JoinCode.Gui.WebHost;

/// <summary>job/list 流请求（args 直传，非 request 包装）</summary>
internal sealed class DshJobListArgs {
    /// <summary>会话 ID（可见集合 = 该会话的后台任务）</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>job/follow 流请求</summary>
internal sealed class DshJobFollowArgs {
    /// <summary>会话 ID（可空 = 无主任务）</summary>
    public string? SessionId { get; set; }
    /// <summary>任务 ID（即引擎后台代理 ID）</summary>
    public string JobId { get; set; } = "";
    /// <summary>续读字节偏移（缺省 = 最早保留位置）</summary>
    public long? From { get; set; }
}

/// <summary>job/kill 一元请求</summary>
internal sealed class DshJobKillArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>任务 ID</summary>
    public string JobId { get; set; } = "";
}

/// <summary>job/kill 结果</summary>
internal sealed class DshJobKillResult {
    /// <summary>requested = 已受理停止；already-finished = 任务已结束</summary>
    public string Outcome { get; set; } = "requested";
}

/// <summary>任务输出环坐标（DSH 契约）</summary>
internal sealed class DshJobOutputInfo {
    /// <summary>下一块的绝对字节偏移</summary>
    public long Total { get; set; }
    /// <summary>最早保留的字节偏移（保留策略丢头时大于 0）</summary>
    public long Earliest { get; set; }
    /// <summary>溢出文件路径（自有引擎不产生）</summary>
    public List<string> SpillPaths { get; set; } = [];
}

/// <summary>任务只读投影（DSH JobView）</summary>
internal sealed class DshJobView {
    /// <summary>任务 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>任务种类（subagent / bash）</summary>
    public string Kind { get; set; } = "subagent";
    /// <summary>一行标签</summary>
    public string Label { get; set; } = "";
    /// <summary>所属会话</summary>
    public string? Owner { get; set; }
    /// <summary>模型可见输出上限（自有引擎不限）</summary>
    public int? OutputLimitBytes { get; set; }
    /// <summary>running / stopping / completed / killed / failed</summary>
    public string Status { get; set; } = "running";
    /// <summary>实时进度行</summary>
    public string? Progress { get; set; }
    /// <summary>终态原因</summary>
    public string? Detail { get; set; }
    /// <summary>注册时间（Unix 毫秒）</summary>
    public long StartedAt { get; set; }
    /// <summary>结束时间（Unix 毫秒，运行中缺省）</summary>
    public long? FinishedAt { get; set; }
    /// <summary>输出环坐标</summary>
    public DshJobOutputInfo Output { get; set; } = new();
}

/// <summary>job/list 帧：任务花名册快照</summary>
internal sealed class DshJobRowsFrame {
    /// <summary>固定 rows</summary>
    public string Type { get; set; } = "rows";
    /// <summary>当前可见任务</summary>
    public List<DshJobView> Jobs { get; set; } = [];
}

/// <summary>job/follow 首帧</summary>
internal sealed class DshJobOpenedFrame {
    /// <summary>固定 opened</summary>
    public string Type { get; set; } = "opened";
    /// <summary>任务投影</summary>
    public DshJobView Job { get; set; } = new();
    /// <summary>本次跟随起点（字节偏移）</summary>
    public long From { get; set; }
}

/// <summary>任务输出块</summary>
internal sealed class DshJobChunk {
    /// <summary>块首字节绝对偏移</summary>
    public long At { get; set; }
    /// <summary>块文本（原样）</summary>
    public string Text { get; set; } = "";
    /// <summary>stdout / stderr / log（自有引擎统一 log）</summary>
    public string? Channel { get; set; }
    /// <summary>此前字节已丢失</summary>
    public bool? GapBefore { get; set; }
}

/// <summary>job/follow 输出帧</summary>
internal sealed class DshJobOutputFrame {
    /// <summary>固定 output</summary>
    public string Type { get; set; } = "output";
    /// <summary>新增块</summary>
    public List<DshJobChunk> Chunks { get; set; } = [];
    /// <summary>下一块的绝对偏移</summary>
    public long Next { get; set; }
    /// <summary>本帧有丢失</summary>
    public bool? Lossy { get; set; }
}

/// <summary>job/follow 终态帧</summary>
internal sealed class DshJobStatusFrame {
    /// <summary>固定 status</summary>
    public string Type { get; set; } = "status";
    /// <summary>终态任务投影</summary>
    public DshJobView Job { get; set; } = new();
}
