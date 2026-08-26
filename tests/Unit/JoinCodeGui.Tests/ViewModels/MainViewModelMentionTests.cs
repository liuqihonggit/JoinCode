using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Models.Interactive;

using IO.FileSystem;
using JoinCode.Gui.Hosting;
using JoinCode.Gui.Persistence;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// F4 与子代理对话（@提及）GUI 路由测试 —
/// 对齐 CLI ReplLoopStep 两条规则：① @name 消息直发 ② 忙时单代理自动转发。
/// </summary>
public class MainViewModelMentionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>记录转发的会话桩 — 运行列表可配置，流式立即完成</summary>
    private sealed class MentionSession : IJccChatSession
    {
        public List<(string AgentId, string Message)> Forwards { get; } = [];
        public List<JoinCode.Gui.ViewModels.BackgroundAgentInfo> Running { get; set; } = [];
        public Func<string, string?>? Finder { get; set; }

        public Task<string?> FindSubAgentIdByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Finder?.Invoke(name));

        public async Task<bool> ForwardInputToSubAgentAsync(string agentId, string message, CancellationToken cancellationToken = default)
        {
            Forwards.Add((agentId, message));
            await Task.Yield();
            return true;
        }

        public Task<IReadOnlyList<JoinCode.Gui.ViewModels.BackgroundAgentInfo>> GetBackgroundAgentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JoinCode.Gui.ViewModels.BackgroundAgentInfo>>(Running.ToList());

        // === 其余成员委托占位实现（与 FakeSession 同构，DIM 成员不重复列出） ===
        public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
        public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
        public bool IsReady => true;
        public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
        public event Action? ExitRequested;
#pragma warning restore CS0067
        public Func<string, bool>? SlashConfirmHandler { get; set; }
        public string CurrentVendor => "fake";
        public string CurrentModelId => "fake-model";
        public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["fake"] = ["fake-model"] };
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>放行被门控的流式回合（测试控制忙态窗口）</summary>
        public void ReleaseGate() => _release.TrySetResult();

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 门控：模拟长回合，测试显式 ReleaseGate 放行（替代盲等）
            await _release.Task.WaitAsync(cancellationToken);
            yield return ChatStreamEvent.Done();
        }
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RewindResult());
        public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RefreshVendorModelMap() { }
        public void SwitchSession(string sessionId) { }
        public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public EffortLevel EffortLevel => EffortLevel.Auto;
        public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public float? Temperature => null;
        public int? MaxTokens => null;
        public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
        public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static MainViewModel CreateVm(MentionSession session) => new(
        session,
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    [Fact]
    public async Task Mention_WhenAgentFound_ShouldForwardWithoutNewTurn()
    {
        var session = new MentionSession
        {
            Finder = name => name == "explore" ? "agent-1" : null,
            Running = [new("agent-1", "explore", "调研", "running", DateTime.Now, 0, 0)]
        };
        var vm = CreateVm(session);
        vm.InputText = "@explore 帮我查README";

        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

        session.Forwards.Should().ContainSingle(f => f.AgentId == "agent-1" && f.Message == "帮我查README");
        vm.Messages.Should().Contain(m => m.Role == MessageRole.System && m.Content.Contains("已转发给 @explore"));
        vm.IsBusy.Should().BeFalse("转发不开新 LLM 回合");
        vm.Messages.Should().NotContain(m => m.IsStreaming && m.Kind == ChatUiMessageKind.Text && m.Role == MessageRole.Assistant,
            "转发路径不得创建助手流式占位");
        vm.InputText.Should().BeEmpty();
    }

    [Fact]
    public async Task Mention_WhenNotFound_ShouldEchoRunningList()
    {
        var session = new MentionSession
        {
            Finder = _ => null,
            Running = [new("a9", "planner", "规划", "running", DateTime.Now, 0, 0)]
        };
        var vm = CreateVm(session);
        vm.InputText = "@ghost 你好";

        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

        session.Forwards.Should().BeEmpty();
        vm.Messages.Should().Contain(m => m.Content.Contains("未找到子代理 @ghost") && m.Content.Contains("planner"));
    }

    [Fact]
    public async Task BusySend_WithSingleRunningAgent_ShouldAutoForward()
    {
        var session = new MentionSession
        {
            Running = [new("solo", "worker", "干活", "running", DateTime.Now, 0, 0)]
        };
        var vm = CreateVm(session);

        // 第一条：正常回合（门控保持忙态，事件驱动等待而非盲等）
        vm.InputText = "开始任务";
        var turnTask = Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        await WaitBusyAsync(vm, targetBusy: true);

        // 忙时第二条普通消息 → 自动转发给唯一运行代理
        vm.InputText = "补充信息";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

        session.Forwards.Should().ContainSingle(f => f.AgentId == "solo" && f.Message == "补充信息");
        vm.Messages.Should().Contain(m => m.Content.Contains("已转发给 @worker"));

        session.ReleaseGate();
        await turnTask;
        vm.IsBusy.Should().BeFalse();
    }

    /// <summary>事件驱动等待 IsBusy 到达目标态（替代 Task.Delay 盲等，JCC3010）</summary>
    private static Task WaitBusyAsync(MainViewModel vm, bool targetBusy)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.IsBusy))
                return;
            if (vm.IsBusy == targetBusy)
            {
                vm.PropertyChanged -= Handler;
                tcs.TrySetResult();
            }
        }
        vm.PropertyChanged += Handler;
        if (vm.IsBusy == targetBusy)
        {
            vm.PropertyChanged -= Handler;
            tcs.TrySetResult();
        }
        return tcs.Task;
    }
}
