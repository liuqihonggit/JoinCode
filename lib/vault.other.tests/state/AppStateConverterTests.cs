
namespace Core.Tests.State;

/// <summary>
/// AppStateConverter 双向映射确定性测试
/// 验证 ToDocument/FromDocument 往返一致性: FromDocument(ToDocument(s)) 在可持久化字段上 == s
/// 注意: Ui/Mcp/Bridge/Permission/ThinkingEnabled/AgentType 不参与持久化,往返后会重置
/// </summary>
public sealed class AppStateConverterTests {

    private static AppState CreateSampleState() => new() {
        Session = new SessionState {
            SessionId = "session-001",
            SystemPrompt = "You are helpful",
            MessageList = ImmutableList.Create(
                new ApiMessageState { Role = "user", Content = "Hi", Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Metadata = ImmutableHamT.CreateRange(new Dictionary<string, string> { ["k1"] = "v1" }) },
                new ApiMessageState { Role = "assistant", Content = "Hello", Timestamp = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc) }),
            StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastActivityAt = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            CurrentModel = "gpt-4o",
            IsPlanMode = true,
            CurrentPlan = "plan-1"
        },
        Agents = ImmutableHamT.CreateRange(new Dictionary<string, AgentState> {
            ["agent-1"] = new() {
                AgentId = "agent-1", Name = "Alpha", Role = AgentRole.Coordinator, Status = AgentStatus.Running,
                WorkingDirectory = "/work", CurrentTaskId = "task-1",
                Metadata = ImmutableHamT.CreateRange(new Dictionary<string, string> { ["team"] = "x" }),
                LastActivityAt = new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc)
            }
        }),
        Tasks = ImmutableHamT.CreateRange(new Dictionary<string, JoinCode.Abstractions.State.TaskState> {
            ["task-1"] = new() {
                TaskId = "task-1", Name = "Task One", Description = "desc",
                Status = TaskExecutionStatus.Running, AgentId = "agent-1",
                SubTaskIds = ImmutableList.Create("task-2"),
                Progress = 50, Result = "ok",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                StartedAt = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc),
                Metadata = ImmutableHamT.CreateRange(new Dictionary<string, string> { ["p"] = "1" })
            }
        }),
        Config = new ConfigState {
            DebugLog = true, IsBriefMode = false, Theme = "dark",
            AutoConfirm = true, MaxTokenBudget = 100000, UsedTokens = 5000,
            Settings = ImmutableHamT.CreateRange(new Dictionary<string, string> { ["lang"] = "zh" })
        }
    };

    // === ToDocument ===

    [Fact]
    public void ToDocument_PreservesSessionFields() {
        var state = CreateSampleState();
        var doc = AppStateConverter.ToDocument(state);
        doc.Session.SessionId.Should().Be("session-001");
        doc.Session.SystemPrompt.Should().Be("You are helpful");
        doc.Session.CurrentModel.Should().Be("gpt-4o");
        doc.Session.IsPlanMode.Should().BeTrue();
        doc.Session.CurrentPlan.Should().Be("plan-1");
        doc.Session.MessageList.Should().HaveCount(2);
    }

    [Fact]
    public void ToDocument_PreservesAgentFields() {
        var state = CreateSampleState();
        var doc = AppStateConverter.ToDocument(state);
        doc.Agents.Should().ContainKey("agent-1");
        var agent = doc.Agents["agent-1"];
        agent.AgentId.Should().Be("agent-1");
        agent.Name.Should().Be("Alpha");
        agent.Status.Should().Be(AgentStatus.Running);
        agent.WorkingDirectory.Should().Be("/work");
        agent.Metadata.Should().Contain(new KeyValuePair<string, string>("team", "x"));
    }

    [Fact]
    public void ToDocument_PreservesTaskFields() {
        var state = CreateSampleState();
        var doc = AppStateConverter.ToDocument(state);
        doc.Tasks.Should().ContainKey("task-1");
        var task = doc.Tasks["task-1"];
        task.TaskId.Should().Be("task-1");
        task.Name.Should().Be("Task One");
        task.Status.Should().Be(TaskExecutionStatus.Running);
        task.Progress.Should().Be(50);
        task.SubTaskIds.Should().Contain("task-2");
    }

    [Fact]
    public void ToDocument_PreservesConfigFields() {
        var state = CreateSampleState();
        var doc = AppStateConverter.ToDocument(state);
        doc.Config.DebugLog.Should().BeTrue();
        doc.Config.Theme.Should().Be("dark");
        doc.Config.MaxTokenBudget.Should().Be(100000);
        doc.Config.UsedTokens.Should().Be(5000);
        doc.Config.Settings.Should().Contain(new KeyValuePair<string, string>("lang", "zh"));
    }

    [Fact]
    public void ToDocument_SetsIdToCurrentAndVersionTo1() {
        var doc = AppStateConverter.ToDocument(new AppState());
        doc.Id.Should().Be("current");
        doc.Version.Should().Be(1);
    }

    [Fact]
    public void ToDocument_UsesProvidedSavedAt() {
        var savedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var doc = AppStateConverter.ToDocument(new AppState(), savedAt);
        doc.SavedAt.Should().Be(savedAt);
    }

    // === FromDocument(空文档)===

    [Fact]
    public void FromDocument_EmptyDoc_ReturnsDefaultState() {
        var doc = new AppStateDocument();
        var state = AppStateConverter.FromDocument(doc);
        state.Session.SessionId.Should().BeEmpty();
        state.Agents.Should().BeEmpty();
        state.Tasks.Should().BeEmpty();
    }

    [Fact]
    public void FromDocument_NullAgents_ReturnsEmptyAgents() {
        var doc = new AppStateDocument { Agents = null! };
        var state = AppStateConverter.FromDocument(doc);
        state.Agents.Should().BeEmpty();
    }

    [Fact]
    public void FromDocument_NullTasks_ReturnsEmptyTasks() {
        var doc = new AppStateDocument { Tasks = null! };
        var state = AppStateConverter.FromDocument(doc);
        state.Tasks.Should().BeEmpty();
    }

    // === 往返一致性: FromDocument(ToDocument(s)) == s(可持久化字段)===

    [Fact]
    public void RoundTrip_SessionFields_Preserved() {
        var original = CreateSampleState();
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        roundTripped.Session.SessionId.Should().Be(original.Session.SessionId);
        roundTripped.Session.SystemPrompt.Should().Be(original.Session.SystemPrompt);
        roundTripped.Session.StartedAt.Should().Be(original.Session.StartedAt);
        roundTripped.Session.LastActivityAt.Should().Be(original.Session.LastActivityAt);
        roundTripped.Session.CurrentModel.Should().Be(original.Session.CurrentModel);
        roundTripped.Session.IsPlanMode.Should().Be(original.Session.IsPlanMode);
        roundTripped.Session.CurrentPlan.Should().Be(original.Session.CurrentPlan);
        roundTripped.Session.MessageList.Should().HaveCount(original.Session.MessageList.Count);
    }

    [Fact]
    public void RoundTrip_MessageMetadata_Preserved() {
        var original = CreateSampleState();
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        var origMeta = original.Session.MessageList[0].Metadata;
        var rtMeta = roundTripped.Session.MessageList[0].Metadata;
        rtMeta.Should().BeEquivalentTo(origMeta);
    }

    [Fact]
    public void RoundTrip_Agents_Preserved() {
        var original = CreateSampleState();
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        roundTripped.Agents.Should().HaveCount(1);
        roundTripped.Agents.Should().ContainKey("agent-1");
        var orig = original.Agents["agent-1"];
        var rt = roundTripped.Agents["agent-1"];
        rt.AgentId.Should().Be(orig.AgentId);
        rt.Name.Should().Be(orig.Name);
        rt.Role.Should().Be(orig.Role);
        rt.Status.Should().Be(orig.Status);
        rt.WorkingDirectory.Should().Be(orig.WorkingDirectory);
        rt.CurrentTaskId.Should().Be(orig.CurrentTaskId);
        rt.LastActivityAt.Should().Be(orig.LastActivityAt);
        rt.Metadata.Should().BeEquivalentTo(orig.Metadata);
    }

    [Fact]
    public void RoundTrip_Tasks_Preserved() {
        var original = CreateSampleState();
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        roundTripped.Tasks.Should().HaveCount(1);
        var orig = original.Tasks["task-1"];
        var rt = roundTripped.Tasks["task-1"];
        rt.TaskId.Should().Be(orig.TaskId);
        rt.Name.Should().Be(orig.Name);
        rt.Description.Should().Be(orig.Description);
        rt.Status.Should().Be(orig.Status);
        rt.AgentId.Should().Be(orig.AgentId);
        rt.Progress.Should().Be(orig.Progress);
        rt.Result.Should().Be(orig.Result);
        rt.SubTaskIds.Should().BeEquivalentTo(orig.SubTaskIds);
        rt.Metadata.Should().BeEquivalentTo(orig.Metadata);
    }

    [Fact]
    public void RoundTrip_Config_Preserved() {
        var original = CreateSampleState();
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        roundTripped.Config.DebugLog.Should().Be(original.Config.DebugLog);
        roundTripped.Config.IsBriefMode.Should().Be(original.Config.IsBriefMode);
        roundTripped.Config.Theme.Should().Be(original.Config.Theme);
        roundTripped.Config.AutoConfirm.Should().Be(original.Config.AutoConfirm);
        roundTripped.Config.MaxTokenBudget.Should().Be(original.Config.MaxTokenBudget);
        roundTripped.Config.UsedTokens.Should().Be(original.Config.UsedTokens);
        roundTripped.Config.Settings.Should().BeEquivalentTo(original.Config.Settings);
    }

    [Fact]
    public void RoundTrip_UiMcpBridgePermission_ResetToDefault() {
        var original = CreateSampleState() with {
            Ui = new UiState { StatusLineText = "busy", IsLoading = true }
        };
        var roundTripped = AppStateConverter.FromDocument(AppStateConverter.ToDocument(original));
        // 这些字段不参与持久化,往返后重置为默认
        roundTripped.Ui.StatusLineText.Should().BeNull();
        roundTripped.Ui.IsLoading.Should().BeFalse();
    }

    // === 确定性 ===

    [Fact]
    public void ToDocument_Deterministic_SameInputSameOutput() {
        var state = CreateSampleState();
        var d1 = AppStateConverter.ToDocument(state);
        var d2 = AppStateConverter.ToDocument(state);
        d1.Session.SessionId.Should().Be(d2.Session.SessionId);
        d1.Agents.Count.Should().Be(d2.Agents.Count);
        d1.Tasks.Count.Should().Be(d2.Tasks.Count);
    }

    [Fact]
    public void RoundTrip_Deterministic_DoubleRoundTripStable() {
        var state = CreateSampleState();
        var doc1 = AppStateConverter.ToDocument(state);
        var state1 = AppStateConverter.FromDocument(doc1);
        var doc2 = AppStateConverter.ToDocument(state1);
        var state2 = AppStateConverter.FromDocument(doc2);
        // 二次往返应稳定
        state2.Session.SessionId.Should().Be(state1.Session.SessionId);
        state2.Agents.Count.Should().Be(state1.Agents.Count);
    }
}
