namespace JoinCode.Gui.WebHost;

/// <summary>
/// 侧栏终端管理器（TASK066 C3）：按会话持有 ConPTY 终端，维护输出环、屏幕恢复基线与
/// 独占写权限 attachment；进程生命周期与前端连接解耦（follow 断开不杀进程，close 才终止）。
/// </summary>
internal sealed class DshTerminalService {
    /// <summary>单会话终端上限（对齐 DSH maxTerminals）</summary>
    internal const int MaxTerminals = 8;
    /// <summary>单次输入字节上限</summary>
    internal const int MaxInputBytes = 64 * 1024;
    /// <summary>最大列数</summary>
    internal const int MaxCols = 500;
    /// <summary>最大行数</summary>
    internal const int MaxRows = 200;
    /// <summary>保留的回滚行数（前端 xterm 配置）</summary>
    internal const int Scrollback = 5000;
    /// <summary>屏幕恢复基线最多保留的原始字符数（超出丢头部；下一帧标记 lossy 由前端重绘）</summary>
    private const int MaxReplayChars = 200_000;

    private readonly Func<string, CancellationToken, Task<string?>> _cwdProvider;
    private readonly Func<string, IReadOnlyList<string>, string, int, int, IDshPty> _spawn;
    private readonly Func<IReadOnlyList<DshTerminalShellProfile>> _shells;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, DshTerminalSession>> _owners = new(StringComparer.Ordinal);

    /// <summary>生产构造：会话工作目录来自会话存储，PTY 用 ConPTY</summary>
    /// <param name="services">宿主服务容器</param>
    internal DshTerminalService(DshHostServices services) {
        _cwdProvider = services.Sessions.GetSessionCwdAsync;
        _spawn = (path, args, cwd, cols, rows) => DshConPty.Start(path, args, cwd, cols, rows);
        _shells = DiscoverShells;
    }

    /// <summary>测试构造：注入工作目录、PTY 工厂与 shell 清单</summary>
    /// <param name="cwdProvider">会话工作目录</param>
    /// <param name="spawn">PTY 工厂</param>
    /// <param name="shells">shell 清单（首项为默认）</param>
    internal DshTerminalService(
        Func<string, CancellationToken, Task<string?>> cwdProvider,
        Func<string, IReadOnlyList<string>, string, int, int, IDshPty> spawn,
        IReadOnlyList<DshTerminalShellProfile> shells) {
        _cwdProvider = cwdProvider;
        _spawn = spawn;
        _shells = () => shells;
    }

    /// <summary>读取会话工作目录与终端限制（不解析 shell）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshTerminalEnvironmentResult> EnvironmentAsync(string sessionId, CancellationToken cancellationToken) =>
        new() {
            Cwd = await ResolveCwdAsync(sessionId, cancellationToken),
            MaxInputBytes = MaxInputBytes,
            MaxCols = MaxCols,
            MaxRows = MaxRows,
            Scrollback = Scrollback,
        };

    /// <summary>已安装的 shell（默认 shell 在最前）</summary>
    internal IReadOnlyList<DshTerminalShellProfile> Shells() => _shells();

    /// <summary>该会话保留的终端元数据</summary>
    /// <param name="sessionId">会话 ID</param>
    internal IReadOnlyList<DshTerminalInfo> List(string sessionId) {
        lock (_gate) {
            return _owners.TryGetValue(sessionId, out var terminals)
                ? terminals.Values.Select(session => Clone(session.Info)).ToArray()
                : [];
        }
    }

    /// <summary>创建（或幂等返回）终端</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="request">创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshTerminalInfo> CreateAsync(string sessionId, DshTerminalCreateRequest request, CancellationToken cancellationToken) {
        ValidateId(request.Id);
        ValidateDimensions(request.Cols, request.Rows);
        var shell = ResolveShell(request.ShellPath);
        var cwd = await ResolveCwdAsync(sessionId, cancellationToken);
        var existing = FindExisting(sessionId, request.Id);
        if (existing is not null) {
            return existing;
        }
        EnsureCapacity(sessionId);
        var pty = _spawn(shell.Path, shell.Args, cwd, request.Cols, request.Rows);
        var session = new DshTerminalSession(request.Id, new DshTerminalInfo {
            Id = request.Id,
            Shell = shell,
            Title = shell.Name,
            Cwd = cwd,
            Cols = request.Cols,
            Rows = request.Rows,
            State = "running",
            ExitCode = null,
        }, pty);
        session.Pty.Output += data => OnOutput(session, data);
        session.Pty.Exited += code => OnExit(session, code);
        var (registered, duplicate) = Register(sessionId, session);
        if (duplicate is not null) {
            await duplicate.Pty.DisposeAsync().ConfigureAwait(false);
        }
        return Clone(registered.Info);
    }

    /// <summary>已有同 ID 终端（返回元数据副本；不存在为 null）</summary>
    private DshTerminalInfo? FindExisting(string sessionId, string id) {
        lock (_gate) {
            return _owners.TryGetValue(sessionId, out var terminals) && terminals.TryGetValue(id, out var terminal)
                ? Clone(terminal.Info)
                : null;
        }
    }

    /// <summary>容量校验：达到上限抛引导错误</summary>
    private void EnsureCapacity(string sessionId) {
        lock (_gate) {
            var count = _owners.TryGetValue(sessionId, out var terminals) ? terminals.Count : 0;
            if (count >= MaxTerminals) {
                throw new DshTerminalException("terminal/limit-reached",
                    $"会话终端数量已达上限 {MaxTerminals}；请先关闭一个不再使用的终端再新建");
            }
        }
    }

    /// <summary>登记终端；并发创建同 ID 时返回既有终端与新终端（由调用方释放后者）</summary>
    private (DshTerminalSession Registered, DshTerminalSession? Duplicate) Register(string sessionId, DshTerminalSession session) {
        lock (_gate) {
            if (!_owners.TryGetValue(sessionId, out var terminals)) {
                terminals = new Dictionary<string, DshTerminalSession>(StringComparer.Ordinal);
                _owners[sessionId] = terminals;
            }
            if (terminals.TryGetValue(session.Id, out var existing)) {
                return (existing, session);
            }
            terminals[session.Id] = session;
            return (session, null);
        }
    }

    /// <summary>
    /// 打开跟随（取得写权限）：返回屏幕恢复快照与订阅句柄；新 attachment 使旧 attachment 只读。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    /// <param name="attachmentId">attachment 标识</param>
    internal (DshTerminalSnapshotFrame Snapshot, DshTerminalFollower Follower) Follow(string sessionId, string id, string attachmentId) {
        ValidateId(attachmentId);
        DshTerminalSession session;
        DshTerminalFollower follower;
        DshTerminalSnapshotFrame snapshot;
        lock (_gate) {
            session = Require(sessionId, id);
            follower = new DshTerminalFollower(attachmentId);
            snapshot = new DshTerminalSnapshotFrame {
                Sequence = session.Sequence,
                Screen = session.Replay.ToString(),
                Info = Clone(session.Info),
            };
            if (session.Closed || session.Info.State != "running") {
                follower.Push(new DshTerminalStateFrame { Info = Clone(session.Info) });
                follower.Complete();
                return (snapshot, follower);
            }
            session.Followers.Add(follower);
            session.Info = Clone(session.Info);
            session.Info.ControllerId = attachmentId;
            snapshot.Info = Clone(session.Info);
            Broadcast(session, new DshTerminalStateFrame { Info = Clone(session.Info) }, except: follower);
        }
        return (snapshot, follower);
    }

    /// <summary>写入原始输入（仅当前 controller）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    /// <param name="attachmentId">attachment 标识</param>
    /// <param name="data">输入文本</param>
    internal void Write(string sessionId, string id, string attachmentId, string data) {
        if (Encoding.UTF8.GetByteCount(data) > MaxInputBytes) {
            throw new DshTerminalException("terminal/input-too-large",
                $"终端输入超过 {MaxInputBytes} 字节上限；请分批粘贴或改用文件方式传入");
        }
        DshTerminalSession session;
        lock (_gate) {
            session = RequireController(sessionId, id, attachmentId);
        }
        session.Pty.Write(data);
    }

    /// <summary>调整尺寸（仅当前 controller）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    /// <param name="attachmentId">attachment 标识</param>
    /// <param name="cols">列数</param>
    /// <param name="rows">行数</param>
    internal void Resize(string sessionId, string id, string attachmentId, int cols, int rows) {
        ValidateDimensions(cols, rows);
        DshTerminalSession session;
        lock (_gate) {
            session = RequireController(sessionId, id, attachmentId);
            session.Pty.Resize(cols, rows);
            session.Info.Cols = cols;
            session.Info.Rows = rows;
            Broadcast(session, new DshTerminalStateFrame { Info = Clone(session.Info) });
        }
    }

    /// <summary>重命名显示标题</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    /// <param name="title">新标题（1–120 字符）</param>
    internal void Rename(string sessionId, string id, string title) {
        var trimmed = title.Trim();
        if (trimmed.Length is 0 or > 120) {
            throw new DshTerminalException("terminal/rejected", "终端标题需要 1–120 个字符；请修改后重试");
        }
        lock (_gate) {
            var session = Require(sessionId, id);
            session.Info.Title = trimmed;
            Broadcast(session, new DshTerminalStateFrame { Info = Clone(session.Info) });
        }
    }

    /// <summary>关闭终端（幂等）：终止进程并通知所有跟随者</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    internal async Task CloseAsync(string sessionId, string id) {
        var (session, followers) = TakeForClose(sessionId, id);
        foreach (var follower in followers) {
            follower.Dispose();
        }
        if (session is not null) {
            await session.Pty.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>移除终端并收集需要收尾的跟随者（锁内只做内存操作）</summary>
    private (DshTerminalSession? Session, List<DshTerminalFollower> Followers) TakeForClose(string sessionId, string id) {
        lock (_gate) {
            if (!_owners.TryGetValue(sessionId, out var terminals) || !terminals.Remove(id, out var session)) {
                return (null, []);
            }
            session.Closed = true;
            session.Info.State = "exited";
            session.Info.ExitCode ??= 0;
            var followers = new List<DshTerminalFollower>(session.Followers);
            session.Followers.Clear();
            foreach (var follower in followers) {
                follower.Push(new DshTerminalStateFrame { Info = Clone(session.Info) });
                follower.Complete();
            }
            return (session, followers);
        }
    }

    /// <summary>保留声明：终端存在且未关闭即返回 retained（进程生命周期独立于流）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="id">终端 ID</param>
    internal void Retain(string sessionId, string id) {
        lock (_gate) {
            _ = Require(sessionId, id);
        }
    }

    /// <summary>PTY 输出：推进序号、写回放环并广播</summary>
    private void OnOutput(DshTerminalSession session, string data) {
        if (data.Length == 0) {
            return;
        }
        lock (_gate) {
            if (session.Closed) {
                return;
            }
            session.Sequence++;
            session.Replay.Append(data);
            TrimReplay(session.Replay);
            Broadcast(session, new DshTerminalOutputFrame { Sequence = session.Sequence, Data = data });
        }
    }

    /// <summary>PTY 退出：更新状态并结束所有跟随流</summary>
    private void OnExit(DshTerminalSession session, int? exitCode) {
        List<DshTerminalFollower> followers;
        lock (_gate) {
            if (session.Closed) {
                return;
            }
            session.Closed = true;
            session.Info.State = exitCode is null ? "failed" : "exited";
            session.Info.ExitCode = exitCode;
            if (exitCode is null) {
                session.Info.Error = "终端进程意外结束";
            }
            followers = [.. session.Followers];
            session.Followers.Clear();
            foreach (var follower in followers) {
                follower.Push(new DshTerminalStateFrame { Info = Clone(session.Info) });
                follower.Complete();
            }
        }
        foreach (var follower in followers) {
            follower.Dispose();
        }
    }

    /// <summary>广播帧给除 except 外的所有跟随者；队列满则关闭该跟随者（DSH 慢跟随者显式失败）</summary>
    private static void Broadcast(DshTerminalSession session, object frame, DshTerminalFollower? except = null) {
        foreach (var follower in session.Followers) {
            if (!ReferenceEquals(follower, except)) {
                follower.Push(frame);
            }
        }
    }

    /// <summary>回放环超限时从头部丢弃（保证截断处落在转义序列边界之外尽可能安全）</summary>
    private static void TrimReplay(StringBuilder replay) {
        if (replay.Length <= MaxReplayChars) {
            return;
        }
        var excess = replay.Length - MaxReplayChars;
        replay.Remove(0, excess);
    }

    /// <summary>解析 shell（path 精确匹配；缺省取第一项）</summary>
    private DshTerminalShellProfile ResolveShell(string? shellPath) {
        var shells = _shells();
        if (shellPath is null) {
            return shells.Count > 0
                ? shells[0]
                : throw new DshTerminalException("terminal/unavailable", "宿主未找到可用 shell；请安装 PowerShell 或 cmd.exe 后重试");
        }
        return shells.FirstOrDefault(shell => string.Equals(shell.Path, shellPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new DshTerminalException("terminal/rejected",
                $"指定的 shell 不在可用清单中：{shellPath}；请从 terminal/shells 返回的列表中选择");
    }

    /// <summary>解析会话工作目录（缺省/失效回退宿主当前目录）</summary>
    private async Task<string> ResolveCwdAsync(string sessionId, CancellationToken cancellationToken) {
        var cwd = await _cwdProvider(sessionId, cancellationToken);
        // meta.json 可能来自 Unix 风格写入（正斜杠），CreateProcess 的 lpCurrentDirectory 要求平台分隔符
        return !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd)
            ? Path.GetFullPath(cwd)
            : Environment.CurrentDirectory;
    }

    /// <summary>取终端（不存在 → terminal/unavailable 引导）</summary>
    private DshTerminalSession Require(string sessionId, string id) =>
        _owners.TryGetValue(sessionId, out var terminals) && terminals.TryGetValue(id, out var session)
            ? session
            : throw new DshTerminalException("terminal/unavailable",
                $"终端 {id} 在会话 {sessionId} 中不存在；请刷新终端列表后重新打开");

    /// <summary>取终端并校验写权限</summary>
    private DshTerminalSession RequireController(string sessionId, string id, string attachmentId) {
        var session = Require(sessionId, id);
        if (session.Info.State != "running") {
            throw new DshTerminalException("terminal/control-unavailable",
                $"终端 {id} 已结束（{session.Info.State}）；请新建终端后重试");
        }
        if (!string.Equals(session.Info.ControllerId, attachmentId, StringComparison.Ordinal)) {
            throw new DshTerminalException("terminal/control-unavailable",
                $"终端 {id} 的输入由另一个窗口持有（attachmentId={session.Info.ControllerId}）；请在当前窗口重新 attach 后再输入");
        }
        return session;
    }

    /// <summary>校验终端/attachment 标识（字母数字下划线连字符，1–128）</summary>
    private static void ValidateId(string id) {
        if (id.Length is 0 or > 128 || !id.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')) {
            throw new DshTerminalException("terminal/rejected",
                $"终端标识非法：{id}；只允许 1–128 位字母、数字、下划线和连字符");
        }
    }

    /// <summary>校验尺寸</summary>
    private static void ValidateDimensions(int cols, int rows) {
        if (cols is < 2 or > MaxCols || rows is < 1 or > MaxRows) {
            throw new DshTerminalException("terminal/rejected",
                $"终端尺寸非法：{cols}x{rows}；列需在 2–{MaxCols}，行需在 1–{MaxRows} 之间");
        }
    }

    /// <summary>复制元数据（避免共享可变对象被后续状态覆盖）</summary>
    private static DshTerminalInfo Clone(DshTerminalInfo info) => new() {
        Id = info.Id,
        Shell = info.Shell,
        Title = info.Title,
        Cwd = info.Cwd,
        Cols = info.Cols,
        Rows = info.Rows,
        State = info.State,
        ExitCode = info.ExitCode,
        Error = info.Error,
        ControllerId = info.ControllerId,
    };

    /// <summary>发现已安装 shell：pwsh → powershell → cmd（首项为默认）</summary>
    private static IReadOnlyList<DshTerminalShellProfile> DiscoverShells() {
        var candidates = new[] {
            new DshTerminalShellProfile { Path = FindExecutable("pwsh.exe") ?? "", Args = ["-NoLogo"], Name = "PowerShell 7" },
            new DshTerminalShellProfile { Path = FindExecutable("powershell.exe") ?? "", Args = ["-NoLogo"], Name = "Windows PowerShell" },
            new DshTerminalShellProfile { Path = FindExecutable("cmd.exe") ?? "", Args = [], Name = "Command Prompt" },
        };
        return candidates.Where(shell => shell.Path.Length > 0).ToArray();
    }

    /// <summary>在 PATH 中查找可执行文件</summary>
    private static string? FindExecutable(string fileName) {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), fileName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>单终端状态（Followers/Sequence/Replay 均由 manager 的锁保护）</summary>
    private sealed class DshTerminalSession(string id, DshTerminalInfo info, IDshPty pty) {
        internal string Id { get; } = id;
        internal DshTerminalInfo Info { get; set; } = info;
        internal IDshPty Pty { get; } = pty;
        internal HashSet<DshTerminalFollower> Followers { get; } = [];
        internal StringBuilder Replay { get; } = new();
        internal long Sequence { get; set; }
        internal bool Closed { get; set; }
    }
}

/// <summary>终端操作失败（可映射稳定 DSH 错误码）</summary>
/// <param name="Code">稳定错误码</param>
/// <param name="Message">含下一步引导的说明</param>
internal sealed class DshTerminalException(string code, string message) : Exception(message) {
    /// <summary>稳定错误码</summary>
    internal string Code { get; } = code;
}

/// <summary>
/// 终端跟随者（每个 WebSocket 流一个）：有界帧队列，慢消费者被关闭后可由前端重新 attach 恢复。
/// </summary>
internal sealed class DshTerminalFollower(string attachmentId) : IDisposable {
    private readonly Channel<object> _frames = Channel.CreateBounded<object>(
        new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private int _closed;

    /// <summary>attachment 标识（写权限归属）</summary>
    internal string AttachmentId { get; } = attachmentId;

    /// <summary>帧读取端（output/state）</summary>
    internal ChannelReader<object> Frames => _frames.Reader;

    /// <summary>入队一帧；队列满说明前端跟不上，关闭跟随（重新 attach 从屏幕基线恢复）</summary>
    internal void Push(object frame) {
        if (Volatile.Read(ref _closed) == 0 && !_frames.Writer.TryWrite(frame)) {
            Complete();
        }
    }

    /// <summary>结束帧流</summary>
    internal void Complete() {
        if (Interlocked.Exchange(ref _closed, 1) == 0) {
            _frames.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Complete();
}
