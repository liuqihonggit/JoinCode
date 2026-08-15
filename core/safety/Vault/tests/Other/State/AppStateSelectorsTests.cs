
namespace Core.Tests.State;

/// <summary>
/// AppStateSelectors 单元测试 — 验证各类选择器正确提取派生状态
/// </summary>
public sealed class AppStateSelectorsTests : IDisposable
{
    private readonly Store<AppState> _store;
    private readonly FakeTelemetryService _telemetry;
    private readonly AppStateSelectors _selectors;

    public AppStateSelectorsTests()
    {
        _store = new Store<AppState>(CreateSampleState());
        _telemetry = new FakeTelemetryService();
        _selectors = new AppStateSelectors(_store, _telemetry);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    #region Session Selectors

    [Fact]
    public void SelectSessionId_ReturnsSessionId()
    {
        var selector = _selectors.SelectSessionId();

        selector.CurrentValue.Should().Be("session-001");
    }

    [Fact]
    public void SelectSystemPrompt_ReturnsSystemPrompt()
    {
        var selector = _selectors.SelectSystemPrompt();

        selector.CurrentValue.Should().Be("You are a helpful assistant");
    }

    [Fact]
    public void SelectMessageList_ReturnsMessages()
    {
        var selector = _selectors.SelectMessageList();

        selector.CurrentValue.Should().HaveCount(2);
    }

    [Fact]
    public void SelectCurrentModel_ReturnsCurrentModel()
    {
        var selector = _selectors.SelectCurrentModel();

        selector.CurrentValue.Should().Be("gpt-4o");
    }

    [Fact]
    public void SelectIsPlanMode_ReturnsPlanMode()
    {
        var selector = _selectors.SelectIsPlanMode();

        selector.CurrentValue.Should().BeTrue();
    }

    #endregion

    #region Agent Selectors

    [Fact]
    public void SelectAgents_ReturnsAllAgents()
    {
        var selector = _selectors.SelectAgents();

        selector.CurrentValue.Should().HaveCount(2);
        selector.CurrentValue.Should().ContainKey("agent-1");
    }

    [Fact]
    public void SelectAgent_ExistingAgent_ReturnsAgent()
    {
        var selector = _selectors.SelectAgent("agent-1");

        selector.CurrentValue.Should().NotBeNull();
        selector.CurrentValue!.Name.Should().Be("Alpha");
    }

    [Fact]
    public void SelectAgent_NonExistingAgent_ReturnsNull()
    {
        var selector = _selectors.SelectAgent("missing-agent");

        selector.CurrentValue.Should().BeNull();
    }

    [Fact]
    public void SelectRunningAgentCount_ReturnsCorrectCount()
    {
        var selector = _selectors.SelectRunningAgentCount();

        selector.CurrentValue.Should().Be(1);
    }

    [Fact]
    public void SelectActiveAgents_ReturnsAgentsNotIdle()
    {
        var selector = _selectors.SelectActiveAgents();

        selector.CurrentValue.Should().HaveCount(1);
        selector.CurrentValue[0].AgentId.Should().Be("agent-1");
    }

    #endregion

    #region Task Selectors

    [Fact]
    public void SelectTasks_ReturnsAllTasks()
    {
        var selector = _selectors.SelectTasks();

        selector.CurrentValue.Should().HaveCount(3);
    }

    [Fact]
    public void SelectTask_ExistingTask_ReturnsTask()
    {
        var selector = _selectors.SelectTask("task-1");

        selector.CurrentValue.Should().NotBeNull();
        selector.CurrentValue!.Name.Should().Be("Task One");
    }

    [Fact]
    public void SelectTask_NonExistingTask_ReturnsNull()
    {
        var selector = _selectors.SelectTask("missing-task");

        selector.CurrentValue.Should().BeNull();
    }

    [Fact]
    public void SelectRunningTasks_ReturnsRunningTasks()
    {
        var selector = _selectors.SelectRunningTasks();

        selector.CurrentValue.Should().HaveCount(1);
        selector.CurrentValue[0].TaskId.Should().Be("task-1");
    }

    [Fact]
    public void SelectPendingTaskCount_ReturnsPendingCount()
    {
        var selector = _selectors.SelectPendingTaskCount();

        selector.CurrentValue.Should().Be(1);
    }

    [Fact]
    public void SelectCompletedTaskCount_ReturnsCompletedCount()
    {
        var selector = _selectors.SelectCompletedTaskCount();

        selector.CurrentValue.Should().Be(1);
    }

    #endregion

    #region Config Selectors

    [Fact]
    public void SelectDebugLogMode_ReturnsDebugLog()
    {
        var selector = _selectors.SelectDebugLogMode();

        selector.CurrentValue.Should().BeTrue();
    }

    [Fact]
    public void SelectBriefMode_ReturnsBriefMode()
    {
        var selector = _selectors.SelectBriefMode();

        selector.CurrentValue.Should().BeFalse();
    }

    [Fact]
    public void SelectTheme_ReturnsTheme()
    {
        var selector = _selectors.SelectTheme();

        selector.CurrentValue.Should().Be("dark");
    }

    [Fact]
    public void SelectTokenUsage_SelectorReturnsBudgetAndUsed()
    {
        var selector = _selectors.SelectTokenUsage();

        var result = selector.Selector(_store.GetState());

        result.MaxBudget.Should().Be(100000);
        result.Used.Should().Be(5000);
    }

    #endregion

    #region UI Selectors

    [Fact]
    public void SelectStatusLineText_ReturnsStatusText()
    {
        var selector = _selectors.SelectStatusLineText();

        selector.CurrentValue.Should().Be("Ready");
    }

    [Fact]
    public void SelectIsLoading_ReturnsLoadingState()
    {
        var selector = _selectors.SelectIsLoading();

        selector.CurrentValue.Should().BeFalse();
    }

    [Fact]
    public void SelectCurrentNotification_ReturnsNotification()
    {
        var selector = _selectors.SelectCurrentNotification();

        selector.CurrentValue.Should().NotBeNull();
        selector.CurrentValue!.Message.Should().Be("Hello");
    }

    #endregion

    #region MCP Selectors

    [Fact]
    public void SelectMcpServers_ReturnsServers()
    {
        var selector = _selectors.SelectMcpServers();

        selector.CurrentValue.Should().HaveCount(2);
    }

    [Fact]
    public void SelectAvailableTools_ReturnsTools()
    {
        var selector = _selectors.SelectAvailableTools();

        selector.CurrentValue.Should().HaveCount(2);
        selector.CurrentValue.Should().Contain("read_file");
    }

    [Fact]
    public void SelectConnectedMcpServerCount_ReturnsConnectedCount()
    {
        var selector = _selectors.SelectConnectedMcpServerCount();

        selector.CurrentValue.Should().Be(1);
    }

    #endregion

    #region Bridge Selectors

    [Fact]
    public void SelectBridgeConnected_ReturnsConnectionState()
    {
        var selector = _selectors.SelectBridgeConnected();

        selector.CurrentValue.Should().BeTrue();
    }

    [Fact]
    public void SelectBridgeEnabled_ReturnsEnabledState()
    {
        var selector = _selectors.SelectBridgeEnabled();

        selector.CurrentValue.Should().BeTrue();
    }

    #endregion

    #region Permission Selectors

    [Fact]
    public void SelectPermissionMode_ReturnsMode()
    {
        var selector = _selectors.SelectPermissionMode();

        selector.CurrentValue.Should().Be(PermissionMode.Auto);
    }

    [Fact]
    public void SelectPendingPermissions_ReturnsPendingRequests()
    {
        var selector = _selectors.SelectPendingPermissions();

        selector.CurrentValue.Should().HaveCount(1);
    }

    #endregion

    #region Combined Selectors

    [Fact]
    public void SelectSessionOverview_ReturnsCorrectOverview()
    {
        var selector = _selectors.SelectSessionOverview();

        selector.CurrentValue.SessionId.Should().Be("session-001");
        selector.CurrentValue.CurrentModel.Should().Be("gpt-4o");
        selector.CurrentValue.MessageCount.Should().Be(2);
        selector.CurrentValue.IsPlanMode.Should().BeTrue();
    }

    [Fact]
    public void SelectWorkloadOverview_ReturnsCorrectOverview()
    {
        var selector = _selectors.SelectWorkloadOverview();

        selector.CurrentValue.RunningAgents.Should().Be(1);
        selector.CurrentValue.RunningTasks.Should().Be(1);
        selector.CurrentValue.PendingTasks.Should().Be(1);
    }

    #endregion

    #region Metrics

    [Fact]
    public void SelectSessionId_RecordsTelemetry()
    {
        _selectors.SelectSessionId();

        _telemetry.Counters.Should().Contain(c =>
            c.Name == "vault.selector.count" &&
            c.Tags != null &&
            c.Tags["category"] == "session" &&
            c.Tags["selector"] == "sessionId");
    }

    [Fact]
    public void SelectWorkloadOverview_RecordsTelemetry()
    {
        _selectors.SelectWorkloadOverview();

        _telemetry.Counters.Should().Contain(c =>
            c.Name == "vault.selector.count" &&
            c.Tags != null &&
            c.Tags["category"] == "combined" &&
            c.Tags["selector"] == "workloadOverview");
    }

    #endregion

    private static AppState CreateSampleState()
    {
        return new AppState
        {
            Session = new SessionState
            {
                SessionId = "session-001",
                SystemPrompt = "You are a helpful assistant",
                MessageList = ImmutableList.Create(
                    new ApiMessageState { Role = "user", Content = "Hi", Timestamp = DateTime.UtcNow },
                    new ApiMessageState { Role = "assistant", Content = "Hello", Timestamp = DateTime.UtcNow }),
                CurrentModel = "gpt-4o",
                IsPlanMode = true
            },
            Agents = ImmutableDictionary.CreateRange(new Dictionary<string, AgentState>
            {
                ["agent-1"] = new() { AgentId = "agent-1", Name = "Alpha", Status = AgentStatus.Running },
                ["agent-2"] = new() { AgentId = "agent-2", Name = "Beta", Status = AgentStatus.Idle }
            }),
            Tasks = ImmutableDictionary.CreateRange(new Dictionary<string, JoinCode.Abstractions.State.TaskState>
            {
                ["task-1"] = new() { TaskId = "task-1", Name = "Task One", Status = TaskExecutionStatus.Running },
                ["task-2"] = new() { TaskId = "task-2", Name = "Task Two", Status = TaskExecutionStatus.Pending },
                ["task-3"] = new() { TaskId = "task-3", Name = "Task Three", Status = TaskExecutionStatus.Completed }
            }),
            Config = new ConfigState
            {
                DebugLog = true,
                IsBriefMode = false,
                Theme = "dark",
                MaxTokenBudget = 100000,
                UsedTokens = 5000
            },
            Ui = new UiState
            {
                StatusLineText = "Ready",
                IsLoading = false,
                CurrentNotification = new NotificationState { Message = "Hello" }
            },
            Mcp = new McpState
            {
                Servers = ImmutableList.Create(
                    new McpServerState { Name = "server-1", Status = McpConnectionStatus.Connected },
                    new McpServerState { Name = "server-2", Status = McpConnectionStatus.Disconnected }),
                AvailableTools = ImmutableList.Create("read_file", "write_file")
            },
            Bridge = new BridgeState { IsConnected = true, IsEnabled = true },
            Permission = new PermissionState
            {
                PermissionMode = PermissionMode.Auto,
                PendingRequests = ImmutableList.Create(new PermissionRequestState { ToolName = "read_file" })
            }
        };
    }
}
