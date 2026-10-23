namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 侧栏终端管理器（TASK066 C3）：创建/幂等、屏幕基线、写权限归属、状态广播、退出与限额。
/// </summary>
public sealed class DshTerminalServiceTests {
    /// <summary>创建返回元数据并进入列表；同 ID 再创建幂等且不重复 spawn</summary>
    [Fact]
    public async Task CreateAsync_IsIdempotentAndListed() {
        var (service, ptys) = Create();

        var info = await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        var again = await service.CreateAsync("s1", Request("t1"), CancellationToken.None);

        info.Id.Should().Be("t1");
        info.State.Should().Be("running");
        info.Shell.Name.Should().Be("Fake Shell");
        info.Cols.Should().Be(80);
        again.Id.Should().Be("t1");
        ptys.Should().HaveCount(1);
        service.List("s1").Should().ContainSingle(terminal => terminal.Id == "t1");
    }

    /// <summary>follow 返回已产生输出的屏幕基线（原始输出回放）</summary>
    [Fact]
    public async Task FollowAsync_SnapshotContainsReplayAndSequence() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        ptys[0].Emit("hello ");
        ptys[0].Emit("world");

        var (snapshot, follower) = service.Follow("s1", "t1", "attach-a");

        snapshot.Screen.Should().Be("hello world");
        snapshot.Sequence.Should().Be(2);
        snapshot.Info.ControllerId.Should().Be("attach-a");
        follower.Dispose();
    }

    /// <summary>后 attach 的窗口取得写权限，旧 attachment 输入被拒绝并给出引导</summary>
    [Fact]
    public async Task FollowAsync_NewAttachmentStealsWriteControl() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        var (_, first) = service.Follow("s1", "t1", "attach-a");
        var (snapshot, second) = service.Follow("s1", "t1", "attach-b");

        var act = () => service.Write("s1", "t1", "attach-a", "x");

        act.Should().Throw<DshTerminalException>().Which.Code.Should().Be("terminal/control-unavailable");
        service.Write("s1", "t1", "attach-b", "y");
        ptys[0].Writes.Should().Equal("y");
        snapshot.Info.ControllerId.Should().Be("attach-b");
        first.Frames.TryRead(out var takeover).Should().BeTrue();
        takeover.Should().BeOfType<DshTerminalStateFrame>();
        first.Dispose();
        second.Dispose();
    }

    /// <summary>输出/尺寸/重命名都会广播对应帧</summary>
    [Fact]
    public async Task Frames_BroadcastOutputResizeAndRename() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        var (_, follower) = service.Follow("s1", "t1", "attach-a");

        ptys[0].Emit("$ ");
        service.Resize("s1", "t1", "attach-a", 120, 40);
        service.Rename("s1", "t1", "构建终端");

        var frames = Drain(follower);
        frames.OfType<DshTerminalOutputFrame>().Should().ContainSingle()
            .Which.Data.Should().Be("$ ");
        frames.OfType<DshTerminalStateFrame>().Should().HaveCount(2);
        frames.OfType<DshTerminalStateFrame>().Last().Info.Title.Should().Be("构建终端");
        frames.OfType<DshTerminalStateFrame>().Last().Info.Cols.Should().Be(120);
        ptys[0].Resizes.Should().Equal((120, 40));
        follower.Dispose();
    }

    /// <summary>关闭：通知终态、结束跟随流、释放 PTY 并从列表移除</summary>
    [Fact]
    public async Task CloseAsync_NotifiesAndDisposes() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        var (_, follower) = service.Follow("s1", "t1", "attach-a");

        await service.CloseAsync("s1", "t1");

        var frames = Drain(follower);
        frames.OfType<DshTerminalStateFrame>().Should().ContainSingle()
            .Which.Info.State.Should().Be("exited");
        follower.Frames.Completion.IsCompleted.Should().BeTrue();
        ptys[0].Disposed.Should().BeTrue();
        service.List("s1").Should().BeEmpty();
    }

    /// <summary>进程自然退出：状态帧带退出码并结束跟随流</summary>
    [Fact]
    public async Task Exit_UpdatesStateAndCompletesFollower() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        var (_, follower) = service.Follow("s1", "t1", "attach-a");

        ptys[0].Exit(3);

        var state = Drain(follower).OfType<DshTerminalStateFrame>().Should().ContainSingle().Subject;
        state.Info.State.Should().Be("exited");
        state.Info.ExitCode.Should().Be(3);
        follower.Frames.Completion.IsCompleted.Should().BeTrue();
    }

    /// <summary>结束后再 follow：立即得到终态帧与已完成的流</summary>
    [Fact]
    public async Task FollowAsync_ExitedTerminal_CompletesImmediately() {
        var (service, ptys) = Create();
        await service.CreateAsync("s1", Request("t1"), CancellationToken.None);
        ptys[0].Exit(0);

        var (snapshot, follower) = service.Follow("s1", "t1", "attach-a");

        snapshot.Info.State.Should().Be("exited");
        Drain(follower).OfType<DshTerminalStateFrame>().Should().ContainSingle();
        follower.Frames.Completion.IsCompleted.Should().BeTrue();
    }

    /// <summary>终端数量上限与输入字节上限都返回可执行引导</summary>
    [Fact]
    public async Task Limits_RejectWithGuidance() {
        var (service, _) = Create();
        for (var index = 0; index < DshTerminalService.MaxTerminals; index++) {
            await service.CreateAsync("s1", Request($"t{index}"), CancellationToken.None);
        }

        var overflow = async () => await service.CreateAsync("s1", Request("t-extra"), CancellationToken.None);
        var size = () => service.Write("s1", "t0", "attach-a", new string('a', DshTerminalService.MaxInputBytes + 1));

        (await overflow.Should().ThrowAsync<DshTerminalException>()).Which.Code.Should().Be("terminal/limit-reached");
        size.Should().Throw<DshTerminalException>().Which.Code.Should().Be("terminal/input-too-large");
    }

    /// <summary>非法尺寸与未知终端：稳定错误码 + 下一步提示</summary>
    [Fact]
    public async Task Validation_RejectsBadDimensionsAndUnknownTerminal() {
        var (service, _) = Create();

        var badSize = async () => await service.CreateAsync("s1", new DshTerminalCreateRequest { Id = "t1", Cols = 1, Rows = 0 }, CancellationToken.None);
        var follow = () => service.Follow("s1", "missing", "attach-a");

        (await badSize.Should().ThrowAsync<DshTerminalException>()).Which.Message.Should().Contain("尺寸");
        follow.Should().Throw<DshTerminalException>().Which.Code.Should().Be("terminal/unavailable");
    }

    /// <summary>会话工作目录含正斜杠时必须归一为平台全路径再交给 PTY（Windows 下 CreateProcess 不认 /）</summary>
    [Fact]
    public async Task CreateAsync_NormalizesForwardSlashCwdForSpawn() {
        var spawnCwds = new List<string>();
        var shells = new[] {
            new DshTerminalShellProfile { Path = "fake-shell.exe", Args = ["-NoLogo"], Name = "Fake Shell" },
        };
        var service = new DshTerminalService(
            (_, _) => Task.FromResult<string?>(Environment.CurrentDirectory.Replace('\\', '/')),
            (_, _, cwd, _, _) => {
                spawnCwds.Add(cwd);
                return new FakePty();
            },
            shells);

        var info = await service.CreateAsync("s1", Request("t1"), CancellationToken.None);

        spawnCwds.Should().Equal(Path.GetFullPath(Environment.CurrentDirectory));
        info.Cwd.Should().Be(Path.GetFullPath(Environment.CurrentDirectory));
    }

    /// <summary>构造注入了假 PTY 的服务（工作目录取当前目录，避免回退分支掩盖断言）</summary>
    private static (DshTerminalService Service, List<FakePty> Ptys) Create() {
        var ptys = new List<FakePty>();
        var shells = new[] {
            new DshTerminalShellProfile { Path = "fake-shell.exe", Args = ["-NoLogo"], Name = "Fake Shell" },
        };
        var service = new DshTerminalService(
            (_, _) => Task.FromResult<string?>(Environment.CurrentDirectory),
            (_, _, _, _, _) => {
                var pty = new FakePty();
                ptys.Add(pty);
                return pty;
            },
            shells);
        return (service, ptys);
    }

    /// <summary>终端创建请求</summary>
    private static DshTerminalCreateRequest Request(string id) => new() { Id = id, Cols = 80, Rows = 24 };

    /// <summary>取出当前已入队的全部帧</summary>
    private static List<object> Drain(DshTerminalFollower follower) {
        var frames = new List<object>();
        while (follower.Frames.TryRead(out var frame)) {
            frames.Add(frame);
        }
        return frames;
    }

    /// <summary>可手动驱动的 PTY 假实现</summary>
    private sealed class FakePty : IDshPty {
        public event Action<string>? Output;
        public event Action<int?>? Exited;
        public bool HasExited { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> Writes { get; } = [];
        public List<(int Cols, int Rows)> Resizes { get; } = [];

        /// <summary>推一段输出</summary>
        public void Emit(string data) => Output?.Invoke(data);

        /// <summary>模拟进程退出</summary>
        public void Exit(int? code) {
            HasExited = true;
            Exited?.Invoke(code);
        }

        public void Write(string data) => Writes.Add(data);

        public void Resize(int cols, int rows) => Resizes.Add((cols, rows));

        public ValueTask DisposeAsync() {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
