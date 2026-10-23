namespace JoinCode.Gui.WebHost;

/// <summary>terminal/environment、shells 参数（agentId 由协议层注入）</summary>
internal sealed class DshTerminalAgentArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
}

/// <summary>terminal/environment 结果</summary>
internal sealed class DshTerminalEnvironmentResult {
    /// <summary>会话工作目录</summary>
    public string Cwd { get; set; } = "";
    /// <summary>单次输入字节上限</summary>
    public int MaxInputBytes { get; set; } = 64 * 1024;
    /// <summary>最大列数</summary>
    public int MaxCols { get; set; } = 500;
    /// <summary>最大行数</summary>
    public int MaxRows { get; set; } = 200;
    /// <summary>保留的回滚行数</summary>
    public int Scrollback { get; set; } = 5000;
}

/// <summary>shell 配置（path + 参数 + 显示名）</summary>
internal sealed class DshTerminalShellProfile {
    /// <summary>可执行文件绝对路径</summary>
    public string Path { get; set; } = "";
    /// <summary>启动参数</summary>
    public List<string> Args { get; set; } = [];
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
}

/// <summary>终端元数据（DSH WebTerminalInfo）</summary>
internal sealed class DshTerminalInfo {
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>所用 shell</summary>
    public DshTerminalShellProfile Shell { get; set; } = new();
    /// <summary>显示标题</summary>
    public string Title { get; set; } = "";
    /// <summary>工作目录</summary>
    public string Cwd { get; set; } = "";
    /// <summary>列数</summary>
    public int Cols { get; set; }
    /// <summary>行数</summary>
    public int Rows { get; set; }
    /// <summary>running / exited / failed</summary>
    public string State { get; set; } = "running";
    /// <summary>退出码（运行中为 null）</summary>
    public int? ExitCode { get; set; }
    /// <summary>失败原因（无错误时省略字段：DSH 前端以 undefined 判定无错，null 会渲染成“终端错误：null”）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
    /// <summary>当前持有写权限的 attachments</summary>
    public string? ControllerId { get; set; }
}

/// <summary>terminal/create 的 request 体</summary>
internal sealed class DshTerminalCreateRequest {
    /// <summary>前端生成的终端 ID（1–128 位字母数字/下划线/连字符）</summary>
    public string Id { get; set; } = "";
    /// <summary>指定 shell（缺省 = 默认 shell）</summary>
    public string? ShellPath { get; set; }
    /// <summary>列数</summary>
    public int Cols { get; set; }
    /// <summary>行数</summary>
    public int Rows { get; set; }
}

/// <summary>terminal/create 参数</summary>
internal sealed class DshTerminalCreateArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>创建请求</summary>
    public DshTerminalCreateRequest Request { get; set; } = new();
}

/// <summary>terminal/{close,rename} 参数</summary>
internal sealed class DshTerminalIdArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>新标题（rename 用）</summary>
    public string? Title { get; set; }
}

/// <summary>terminal/write 参数</summary>
internal sealed class DshTerminalWriteArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>写权限 attachment</summary>
    public string AttachmentId { get; set; } = "";
    /// <summary>原始输入</summary>
    public string Data { get; set; } = "";
}

/// <summary>terminal/resize 参数</summary>
internal sealed class DshTerminalResizeArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>写权限 attachment</summary>
    public string AttachmentId { get; set; } = "";
    /// <summary>列数</summary>
    public int Cols { get; set; }
    /// <summary>行数</summary>
    public int Rows { get; set; }
}

/// <summary>terminal/list 参数</summary>
internal sealed class DshTerminalListArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>terminal/follow 参数</summary>
internal sealed class DshTerminalFollowArgs {
    /// <summary>会话 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>本次 attachment 标识（取得写权限）</summary>
    public string AttachmentId { get; set; } = "";
}

/// <summary>terminal/retain 参数</summary>
internal sealed class DshTerminalRetainArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>终端 ID</summary>
    public string Id { get; set; } = "";
}

/// <summary>terminal/follow 首帧：屏幕恢复基线</summary>
internal sealed class DshTerminalSnapshotFrame {
    /// <summary>固定 snapshot</summary>
    public string Type { get; set; } = "snapshot";
    /// <summary>已产生的输出序号</summary>
    public long Sequence { get; set; }
    /// <summary>回放基线（原始输出尾部，xterm 重放即可恢复屏幕）</summary>
    public string Screen { get; set; } = "";
    /// <summary>终端元数据</summary>
    public DshTerminalInfo Info { get; set; } = new();
}

/// <summary>terminal/follow 输出帧</summary>
internal sealed class DshTerminalOutputFrame {
    /// <summary>固定 output</summary>
    public string Type { get; set; } = "output";
    /// <summary>输出序号（单调递增）</summary>
    public long Sequence { get; set; }
    /// <summary>原始输出（UTF-8 文本，含 ANSI）</summary>
    public string Data { get; set; } = "";
}

/// <summary>terminal/follow 状态帧</summary>
internal sealed class DshTerminalStateFrame {
    /// <summary>固定 state</summary>
    public string Type { get; set; } = "state";
    /// <summary>终端元数据</summary>
    public DshTerminalInfo Info { get; set; } = new();
}

/// <summary>terminal/retain 首帧</summary>
internal sealed class DshTerminalRetainedFrame {
    /// <summary>固定 retained</summary>
    public string Type { get; set; } = "retained";
}
