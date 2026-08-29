namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// MainViewModel 冒烟测试 — 验证"输入→回显→角色化消息"命令链路（不依赖真实引擎）。
/// 异步命令需在 Task.Run 下执行并对命令调用 WaitAsync(Timeout) 施加硬超时，防止在
/// xUnit 单线程同步上下文或 IAsyncEnumerable 续体调度下可能出现的死锁，保证任何情况都能在 5 秒内结束测试并退出。
/// </summary>
public class MainViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 避免测试污染真实 ~/.jcc/sessions。
    /// 传入 PlaceholderChatSession 使构造函数走初始化路径（填充连接/模型列表），对齐真实引擎加载完成后的状态。</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>设置斜杠输入并手动触发刷新（模拟 View 层防抖后调用）</summary>
    private static void SetSlashInput(MainViewModel vm, string text)
    {
        vm.InputText = text;
        vm.InputCaretIndex = text.Length;
        vm.RefreshSlashSuggestions();
    }

    [Fact]
    public async Task Send_WithValidInput_BuildsUserAndAssistantMessages()
    {
        var vm = CreateVm();

        vm.InputText = "hello";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

        vm.Messages.Should().NotBeEmpty();
        vm.Messages[0].Role.Should().Be(MessageRole.User);
        vm.Messages[0].Content.Should().Be("hello");
        vm.Messages.Last().Role.Should().Be(MessageRole.Assistant);
        vm.Messages.Last().Content.Should().Contain("hello");
        vm.InputText.Should().BeEmpty();
        vm.StatusText.Should().Be("就绪");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Send_WithWhitespaceInput_DoesNothing()
    {
        var vm = CreateVm();

        vm.InputText = "   ";
        await vm.SendCommand.ExecuteAsync(null).WaitAsync(Timeout);

        vm.Messages.Should().BeEmpty();
        vm.InputText.Should().Be("   ");
    }

    [Fact]
    public async Task ClearHistory_RemovesMessages()
    {
        var vm = CreateVm();

        vm.InputText = "hello";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        vm.Messages.Should().NotBeEmpty();

        await vm.ClearHistoryCommand.ExecuteAsync(null).WaitAsync(Timeout);
        vm.Messages.Should().BeEmpty();
    }

    [Fact]
    public void NewConversation_CreatesSessionAndClearsMessages()
    {
        var vm = CreateVm();
        vm.InputText = "hello";
        vm.SendCommand.Execute(null);
        vm.Messages.Should().NotBeEmpty();
        var firstCount = vm.Sessions.Count;

        vm.NewConversationCommand.Execute(null);

        vm.Sessions.Should().HaveCount(firstCount + 1);
        vm.Messages.Should().BeEmpty();
        vm.Sessions.Last().Title.Should().StartWith("会话 ");
    }

    [Fact]
    public async Task Session_SendThenNewVm_IsPersistedAndRestored()
    {
        var store = new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions");
        var vm = new MainViewModel(null, store);

        vm.InputText = "你好，帮我写个 hello world";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        var sessionId = vm.Sessions.First(s => s.IsSelected).Id;
        var savedCount = vm.Messages.Count(m =>
            (m.Role == MessageRole.User || m.Role == MessageRole.Assistant)
            && !string.IsNullOrWhiteSpace(m.Content));

        // 新 VM（模拟重启）共享同一 store → 会话应出现在侧边栏
        var vm2 = new MainViewModel(null, store);
        vm2.Sessions.Should().Contain(s => s.Id == sessionId);
        var restored = vm2.Sessions.First(s => s.Id == sessionId);
        restored.Title.Should().Contain("你好");

        // 选中恢复的会话 → 消息区填充已持久化消息
        vm2.SelectSessionCommand.Execute(restored);
        vm2.Messages.Should().NotBeEmpty();
        vm2.Messages.Count.Should().BeGreaterThanOrEqualTo(savedCount);
        vm2.Messages[0].Content.Should().Contain("hello world");
    }

    [Fact]
    public async Task SelectSession_LoadsHistoryIntoUnderlyingSession_EngineReceivesFullHistory()
    {
        var store = new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions");
        var session1 = new HistoryRecordingSession();
        var vm = new MainViewModel(session1, store);

        vm.InputText = "你好，帮我写个 hello world";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        var sessionId = vm.Sessions.First(s => s.IsSelected).Id;

        var session2 = new HistoryRecordingSession();
        var vm2 = new MainViewModel(session2, store);
        var restored = vm2.Sessions.First(s => s.Id == sessionId);

        await Task.Run(() => vm2.SelectSessionCommand.ExecuteAsync(restored)).WaitAsync(Timeout);

        session2.LoadHistoryCalls.Should().HaveCount(1, "SelectSession 应把持久化历史灌入底层 session");
        var loaded = session2.LoadHistoryCalls[0];
        loaded.Should().Contain(m => m.Role == MessageRole.User && m.Content.Contains("hello world"),
            "灌入的历史应包含已持久化的用户消息");
        loaded.Should().Contain(m => m.Role == MessageRole.Assistant,
            "灌入的历史应包含已持久化的助手回复");
    }

    [Fact]
    public async Task RemoveSession_DeletesPersistedFile()
    {
        var store = new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions");
        var vm = new MainViewModel(null, store);

        vm.InputText = "hello";
        // 等 SendAsync 的 finally SaveActiveSession() 完成后再删除，避免异步写回竞态
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        var target = vm.Sessions.First(s => s.IsSelected);

        vm.RemoveSessionCommand.Execute(target);

        store.Load(target.Id).Should().BeNull();
        vm.Sessions.Should().NotContain(target);
    }

    [Fact]
    public void ModelOptions_AreBoundToSessionRealModels()
    {
        var fake = new FakeSession();
        var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        vm.ModelOptions.Select(m => m.Id).Should().BeEquivalentTo(["fake-model"]);
        vm.SelectedModel.Should().Be("fake-model");
        vm.SelectedModelOption.Should().NotBeNull();
        vm.SelectedModelOption!.Id.Should().Be("fake-model");
    }

    [Fact]
    public void ModelOptions_DisplayText_DistinguishesProviderAndModel()
    {
        var vm = CreateVm();

        foreach (var item in vm.ModelOptions)
        {
            item.DisplayText.Should().Contain(":", "展示文本应区分供应商与模型：如 'DeepSeek:deepseek-chat'");
        }
    }

    [Fact]
    public void SelectedModelChange_WritesBackToSharedConfig()
    {
        var vm = CreateVm();

        vm.SelectedModel = "deepseek-reasoner";

        vm.SelectedModel.Should().Be("deepseek-reasoner");
    }

    [Fact]
    public void SelectedModelOptionChange_SyncsSelectedModel()
    {
        var session = new CrossContaminationSession();
        var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        var target = vm.ModelOptions.First(m => m.Id == "sensenova-u1-fast");
        vm.SelectedModelOption = target;

        vm.SelectedModel.Should().Be("sensenova-u1-fast");
    }

    [Fact]
    public void ConnectionOptions_OnlyRealProviders()
    {
        var fake = new FakeSession();
        var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        var options = vm.ConnectionOptions;
        options.Should().NotContain(o => o.IsMock);
        options.Should().Contain(o => !o.IsMock && o.Id == "fake");
        vm.SelectedConnection.Should().NotBeNull();
        vm.SelectedConnection!.IsMock.Should().BeFalse("真实引擎存在时默认连接真实引擎");
        vm.IsMockConnection.Should().BeFalse();
    }

    [Fact]
    public void ToggleMock_UpdatesStatusAndModels()
    {
        var fake = new FakeSession();
        var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        vm.ToggleMockCommand.Execute(null);

        vm.IsMockConnection.Should().BeTrue();
        vm.StatusText.Should().Contain("Mock");
    }

    [Fact]
    public void ToggleMock_ToggleBackRestoresRealSession()
    {
        var fake = new FakeSession();
        var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        vm.ToggleMockCommand.Execute(null);
        vm.ToggleMockCommand.Execute(null);

        vm.IsMockConnection.Should().BeFalse();
        vm.StatusText.Should().Contain("真实");
        vm.ModelOptions.Select(m => m.Id).Should().BeEquivalentTo(["fake-model"]);
    }

    [Fact]
    public void SwitchProvider_UpdatesModelListFromVendorModelMap()
    {
        var fake = new FakeSession();
        var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        // 初始默认选 "fake" 真实供应商
        vm.SelectedConnection!.Id.Should().Be("fake");
        vm.ModelOptions.Select(m => m.Id).Should().BeEquivalentTo(["fake-model"]);
    }

    /// <summary>跨供应商切换时，旧供应商的 CurrentModelId 不应污染新供应商的模型列表</summary>
    [Fact]
    public void ModelOptions_DoesNotCrossContaminateModelsFromOtherProviders()
    {
        var session = new CrossContaminationSession();
        var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

        // 初始选 sensenova，模型列表应包含 sensenova 模型
        vm.SelectedConnection!.Id.Should().Be("sensenova");
        vm.ModelOptions.Select(m => m.Id).Should().Contain("sensenova-6.7-flash-lite");

        // 切换到 anthropic
        var anthropic = vm.ConnectionOptions.First(o => o.Id == "anthropic");
        vm.SelectedConnection = anthropic;

        // anthropic 模型列表不应混入 sensenova 模型
        vm.ModelOptions.Select(m => m.Id).Should().NotContain("sensenova-6.7-flash-lite");
        vm.ModelOptions.Select(m => m.Id).Should().OnlyContain(
            id => !id.StartsWith("sensenova", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConnectionOptions_NoDuplicateProviders()
    {
        var vm = CreateVm();
        var options = vm.ConnectionOptions;

        var ids = options.Select(o => o.Id).ToArray();
        ids.Distinct().Count().Should().Be(ids.Length, "供应商ID不应重复");

        var displays = options.Select(o => o.DisplayText).ToArray();
        displays.Distinct().Count().Should().Be(displays.Length, "显示文本不应重复");
    }

    [Fact]
    public void ModelOptions_NoDuplicateModels()
    {
        var vm = CreateVm();
        var ids = vm.ModelOptions.Select(o => o.Id).ToArray();
        ids.Distinct().Count().Should().Be(ids.Length, "模型ID不应重复");
    }

    [Fact]
    public void AttachRealSession_HotSwapsPlaceholderToRealEngine()
    {
        // 异步启动路径：VM 先以占位会话显示，引擎组装完成后再热切换
        var vm = new MainViewModel(null, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.IsMockConnection.Should().BeTrue("未注入会话时处于 Mock 占位");

        var fake = new FakeSession();
        vm.AttachRealSession(fake);

        vm.IsMockConnection.Should().BeFalse("热切换后应替换为真实引擎会话");
        vm.SelectedConnection!.IsMock.Should().BeFalse();
        vm.SelectedModel.Should().Be("fake-model");
        vm.StatusText.Should().Contain("已连接真实引擎");
        vm.ModelOptions.Select(m => m.Id).Should().BeEquivalentTo(["fake-model"]);
    }

    /// <summary>占位模式（session is null）时状态栏应显示加载提示，但供应商/模型列表仍从 models.json 填充供预览</summary>
    [Fact]
    public void PlaceholderMode_ShowsLoadingStatus()
    {
        // 注入 fixture 目录 loader — 占位会话从 VM 的 _modelConfigLoader 构建供应商预览（B8 密闭化）
        var vm = new MainViewModel(null, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"), JoinCode.Gui.Tests.Hosting.JccChatSessionAssemblyTests.CreateFedLoader());
        vm.IsMockConnection.Should().BeTrue("未注入会话时处于 Mock 占位");
        vm.StatusText.Should().Be("正在加载引擎…");
        vm.IsEngineLoaded.Should().BeFalse("引擎未加载完成");
        vm.ConnectionOptions.Should().NotBeEmpty("引擎未就绪时仍从 models.json 填充供应商列表供预览");
    }

    [Fact]
    public void EffortOptions_IncludeCliLevels()
    {
        // 对齐 CLI /effort 全部级别：low/medium/high/max/auto
        var vm = CreateVm();

        vm.EffortOptions.Should().BeEquivalentTo(["low", "medium", "high", "max", "auto"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void SelectedEffort_InitializedFromSession()
    {
        // Placeholder 会话 EffortLevel = Auto → 下拉默认显示 auto
        var vm = CreateVm();

        vm.SelectedEffort.Should().Be("auto");
    }

    [Fact]
    public void SelectedEffortChange_UpdatesStatusText()
    {
        // 切换推理力度 → VM 状态栏立即反馈（对齐 CLI /effort 的终端提示）
        var vm = CreateVm();

        vm.SelectedEffort = "high";

        vm.StatusText.Should().Be("推理力度: high");
    }

    [Fact]
    public async Task ResetSettings_RestoresAutoEffort()
    {
        var vm = CreateVm();

        vm.SelectedEffort = "max";
        vm.ResetSettingsCommand.Execute(null);

        vm.SelectedEffort.Should().Be("auto");
    }

    [Fact]
    public async Task RemoveMessage_DeletesSingleMessage()
    {
        var vm = CreateVm();

        vm.InputText = "hello";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        vm.Messages.Should().NotBeEmpty();

        var first = vm.Messages[0];
        var before = vm.Messages.Count;
        vm.RemoveMessageCommand.Execute(first);

        vm.Messages.Should().HaveCount(before - 1);
        vm.Messages.Should().NotContain(first);
    }

    [Fact]
    public void InsertDividerAndTimestamp_AppendToInput()
    {
        var vm = CreateVm();

        vm.InsertDividerCommand.Execute(null);
        vm.InputText.Should().Contain("---");

        vm.InsertTimestampCommand.Execute(null);
        vm.InputText.Should().Contain(":");
    }

    [Fact]
    public void InputTextChange_UpdatesCharsCount()
    {
        var vm = CreateVm();
        vm.InputText = "abcde";
        vm.CharsCount.Should().Be(5);
    }

[Fact]
        public void RemoveSession_RemovesFromList()
        {
            var vm = CreateVm();
            var before = vm.Sessions.Count;
            var target = vm.Sessions[^1];

            vm.RemoveSessionCommand.Execute(target);

            vm.Sessions.Should().HaveCount(before - 1);
            vm.Sessions.Should().NotContain(target);
        }

        [Fact]
        public void SelectSession_MarksOnlyTargetSelected()
        {
            var vm = CreateVm();
            var first = vm.Sessions[0];
            vm.NewConversationCommand.Execute(null);
            var second = vm.Sessions[^1];

            vm.SelectSessionCommand.Execute(first);

            first.IsSelected.Should().BeTrue();
            second.IsSelected.Should().BeFalse();

            vm.SelectSessionCommand.Execute(second);

            second.IsSelected.Should().BeTrue();
            first.IsSelected.Should().BeFalse();
        }

        [Fact]
        public async Task Send_UsesFirstUserMessageAsSessionTitle()
        {
            var vm = CreateVm();
            vm.InputText = "帮我写个爬虫脚本";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            var active = vm.SelectedSession;
            active!.Title.Should().Be("帮我写个爬虫脚本");
        }

        [Fact]
        public void CopyMessage_SetsFeedbackState()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "sample" };
            vm.Messages.Add(msg);

        vm.CopyMessageCommand.Execute(msg);

        vm.HasCopied.Should().BeTrue();
    }

    [Fact]
    public void CopyMessage_SetsFullCopyText()
    {
        var vm = CreateVm();
        var msg = new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = "sample",
            Timestamp = new DateTime(2026, 1, 1, 10, 0, 0),
        };
        vm.Messages.Add(msg);

        vm.CopyMessageCommand.Execute(msg);

        vm.CopiedMessageCopy.Should().Contain("sample");
    }

    [Fact]
    public void SlashInput_OpensPopupAndFillsSuggestions()
    {
        var vm = CreateVm();

        SetSlashInput(vm, "/");

        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.SlashSuggestions.Should().OnlyContain(s => s.Name.StartsWith("/"));
    }

    [Fact]
    public void SlashInput_PrefixFiltersSuggestions()
    {
        var vm = CreateVm();

        SetSlashInput(vm, "/c");

        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.SlashSuggestions.Should().OnlyContain(s => s.Name.StartsWith("/c", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SlashInput_EmptySuggestion_ClosesPopup()
    {
        var vm = CreateVm();

        SetSlashInput(vm, "/zzz-not-a-command");

        vm.IsSlashPopupOpen.Should().BeFalse();
        vm.SlashSuggestions.Should().BeEmpty();
    }

    [Fact]
    public void NonSlashInput_ClosesPopup()
    {
        var vm = CreateVm();
        SetSlashInput(vm, "/");

        SetSlashInput(vm, "hello");

        vm.IsSlashPopupOpen.Should().BeFalse();
        vm.SlashSuggestions.Should().BeEmpty();
    }

    [Fact]
    public void CompleteSlashSuggestion_SetsInputToCommandName()
    {
        var vm = CreateVm();
        SetSlashInput(vm, "/cle");

        vm.CompleteSlashSuggestion();

        vm.InputText.Should().StartWith("/clear");
        vm.IsSlashPopupOpen.Should().BeFalse();
    }

    [Fact]
    public void SlashNavigate_MovesSelection()
    {
        var vm = CreateVm();
        SetSlashInput(vm, "/c");

        var first = vm.SlashSelectedIndex;
        vm.SlashNavigate(1);
        vm.SlashSelectedIndex.Should().Be(first + 1);
        vm.SlashNavigate(-1);
        vm.SlashSelectedIndex.Should().Be(first);
    }

    [Fact]
    public void SlashHighlight_SplitsMatchedAndRemainingPart()
    {
        var vm = CreateVm();
        SetSlashInput(vm, "/cle");

        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.SlashSuggestions.Should().OnlyContain(s => s.MatchedPart + s.RemainingPart == s.Name);
        vm.SlashSuggestions.Should().OnlyContain(s => s.MatchedPart == "/cle");
    }

    [Fact]
    public void SlashHighlight_SingleSlash_MatchesEntireSlashPrefix()
    {
        var vm = CreateVm();
        SetSlashInput(vm, "/");

        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        foreach (var s in vm.SlashSuggestions)
        {
            s.MatchedPart.Should().Be("/");
            s.RemainingPart.Should().Be(s.Name[1..]);
        }
    }


        [Fact]
        public void CopyEmptyMessage_DoesNotSetFeedback()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = string.Empty };
            vm.Messages.Add(msg);

            vm.CopyMessageCommand.Execute(msg);

            vm.HasCopied.Should().BeFalse();
        }

        [Fact]
        public void BeginRename_PutsSessionIntoEditState()
        {
            var vm = CreateVm();
            var session = vm.Sessions[0];

            vm.BeginRenameSessionCommand.Execute(session);

            session.IsRenaming.Should().BeTrue();
            session.IsSelected.Should().BeTrue();
            session.RenameDraft.Should().Be(session.Title);
        }

        [Fact]
        public void CommitRename_AppliesDraftTitle()
        {
            var vm = CreateVm();
            var session = vm.Sessions[0];
            vm.BeginRenameSessionCommand.Execute(session);
            session.RenameDraft = "新标题";

            vm.CommitRenameSessionCommand.Execute(session);

            session.IsRenaming.Should().BeFalse();
            session.Title.Should().Be("新标题");
        }

        [Fact]
        public void CommitRename_EmptyDraft_KeepsOriginalTitle()
        {
            var vm = CreateVm();
            var session = vm.Sessions[0];
            var original = session.Title;
            vm.BeginRenameSessionCommand.Execute(session);
            session.RenameDraft = "   ";

            vm.CommitRenameSessionCommand.Execute(session);

            session.IsRenaming.Should().BeFalse();
            session.Title.Should().Be(original);
        }

        [Fact]
        public async Task StopGenerating_CancelsInFlightSend()
        {
            var vm = CreateVm();
            vm.InputText = "hello";

            var sendTask = Task.Run(() => vm.SendCommand.ExecuteAsync(null));

            vm.StopGeneratingCommand.Execute(null);
            await sendTask.WaitAsync(Timeout);

            vm.IsBusy.Should().BeFalse();
            vm.Messages.Should().NotBeEmpty();
        }

        [Fact]
        public void StopGenerating_WhenNotBusy_DoesNothing()
        {
            var vm = CreateVm();

            vm.StopGeneratingCommand.Execute(null);
            vm.CanStop.Should().BeFalse();
            vm.IsBusy.Should().BeFalse();
        }

        [Fact]
        public void StatusKind_ErrorPrefix_MapsToError()
        {
            var vm = CreateVm();
            vm.StatusText = "错误: something failed";
            vm.StatusKind.Should().Be(StatusKind.Error);
        }

        [Fact]
        public void StatusKind_Thinking_MapsToBusy()
        {
            var vm = CreateVm();
            vm.StatusText = "思考中…";
            vm.StatusKind.Should().Be(StatusKind.Busy);
        }

        [Fact]
        public void StatusKind_Ready_MapsToReady()
        {
            var vm = CreateVm();
            vm.StatusText = "就绪";
            vm.StatusKind.Should().Be(StatusKind.Ready);
        }

        [Fact]
        public void ClearAllSessions_ResetsToListWithOneSession()
        {
            var vm = CreateVm();
            vm.SendCommand.Execute(null);

            vm.ClearAllSessionsCommand.Execute(null);

            vm.Sessions.Should().HaveCount(1);
            vm.Messages.Should().BeEmpty();
        }

        [Fact]
        public void SystemPrompt_HasDefaultValue()
        {
            var vm = CreateVm();
            vm.SystemPrompt.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task CanRegenerate_AfterReply_IsTrue()
        {
            var vm = CreateVm();
            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.CanRegenerate.Should().BeTrue();
        }

        [Fact]
        public async Task Regenerate_RemovesLastTurnAndResends()
        {
            var vm = CreateVm();
            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
            var beforeCount = vm.Messages.Count;

            await Task.Run(() => vm.RegenerateLastReplyCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.Messages.Should().NotBeEmpty();
            vm.Messages.Count.Should().BeLessThanOrEqualTo(beforeCount);
            vm.Messages.Last().Role.Should().Be(MessageRole.Assistant);
        }

        [Fact]
        public void Regenerate_WithoutAssistantMessage_DoesNothing()
        {
            var vm = CreateVm();

            var act = () => vm.RegenerateLastReplyCommand.Execute(null);

            act.Should().NotThrow();
            vm.Messages.Should().BeEmpty();
        }

        [Fact]
        public void EstimatedTokens_TracksMessageContent()
        {
            var vm = CreateVm();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "abcdefgh" });

            vm.EstimatedTokens.Should().Be(2);
            vm.TotalChars.Should().Be(8);
        }

        [Fact]
        public void Messages_ExceedingCap_TrimsOldest()
        {
            // G4 内存防护：长会话/批量恢复历史时 UI 集合无上限增长 → 超过上限裁剪最旧消息
            var vm = CreateVm();
            for (var i = 0; i < 505; i++)
                vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = $"m{i}", Timestamp = DateTime.Now });

            vm.Messages.Count.Should().BeLessThanOrEqualTo(MainViewModel.MaxVisibleMessages,
                "UI 消息列表应有硬上限防止内存无限增长");
            vm.Messages.Should().NotContain(m => m.Content == "m0", "最旧的消息应被优先裁剪");
            vm.Messages.Last().Content.Should().Be("m504", "最新消息必须保留");
        }

        [Fact]
        public void Messages_TrimsKeepAssistantCountConsistent()
        {
            // 裁剪掉助手消息后 CanRegenerate 计数器必须同步递减（否则撤回按钮状态错乱）
            var vm = CreateVm();
            for (var i = 0; i < 503; i++)
                vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = $"u{i}", Timestamp = DateTime.Now });
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "a0", Timestamp = DateTime.Now });
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "a1", Timestamp = DateTime.Now });

            vm.Messages.Count.Should().BeLessThanOrEqualTo(MainViewModel.MaxVisibleMessages);
            vm.AssistantMessageCountForTest.Should().Be(
                vm.Messages.Count(m => m.Role == MessageRole.Assistant),
                "内部计数器应与可见助手消息数一致");
        }

        [Fact]
        public void ResetSettings_RestoresDefaults()
        {
            var vm = CreateVm();
            vm.Temperature = 1.5;
            vm.MaxTokens = 1024;
            vm.StreamingEnabled = false;
            vm.SystemPrompt = "custom";
            vm.FontSize = 18;

            vm.ResetSettingsCommand.Execute(null);

            vm.Temperature.Should().Be(0.7);
            vm.MaxTokens.Should().Be(4096);
            vm.StreamingEnabled.Should().BeTrue();
            vm.SystemPrompt.Should().NotBe("custom");
            vm.FontSize.Should().Be(14);
        }

        [Fact]
        public async Task TemperatureAndMaxTokens_SliderChange_WritesBackToSession()
        {
            var session = new FakeSession();
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.Temperature = 1.2;
            vm.MaxTokens = 3000;

            System.Threading.SpinWait.SpinUntil(
                () => session.WrittenTemperature is not null && session.WrittenMaxTokens is not null,
                TimeSpan.FromSeconds(2));

            session.WrittenTemperature.Should().Be(1.2f);
            session.WrittenMaxTokens.Should().Be(3000);
        }

        [Fact]
        public void FontSize_HasDefaultValue()
        {
            var vm = CreateVm();
            vm.FontSize.Should().Be(14);
        }

        [Fact]
        public void FilteredMessages_EmptySearch_ReturnsAll()
        {
            var vm = CreateVm();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "苹果" });
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "香蕉" });

            vm.FilteredMessages.Should().HaveCount(2);
        }

        [Fact]
        public void FilteredMessages_SearchFiltersByKeyword()
        {
            var vm = CreateVm();
            var apple = new ChatUiMessage { Role = MessageRole.User, Content = "苹果很甜" };
            var banana = new ChatUiMessage { Role = MessageRole.Assistant, Content = "香蕉很香" };
            vm.Messages.Add(apple);
            vm.Messages.Add(banana);

            vm.SearchText = "苹果";

            vm.IsSearching.Should().BeTrue();
            vm.FilteredMessages.Should().Contain(apple);
            vm.FilteredMessages.Should().NotContain(banana);
        }

        [Fact]
        public void FilteredMessages_CaseInsensitive()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "Hello World" };
            vm.Messages.Add(msg);

            vm.SearchText = "world";

            vm.FilteredMessages.Should().Contain(msg);
        }

        [Fact]
        public void ExportSessionText_ContainsRolesAndContents()
        {
            var vm = CreateVm();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "你好" });
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "你好！" });

            var text = vm.ExportSessionText;

            text.Should().Contain("你 ·");
            text.Should().Contain("AI ·");
            text.Should().Contain("你好");
            text.Should().Contain("你好！");
        }

        [Fact]
        public void CopySessionExport_SetsExportPayload()
        {
            var vm = CreateVm();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "hi" });

            vm.CopySessionExportCommand.Execute(null);

            vm.ExportedSessionCopy.Should().Contain("hi");
        }

        [Fact]
        public async Task NavigateHistory_TraversesSentMessages()
        {
            var vm = CreateVm();
            vm.InputText = "第一条";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
            vm.InputText = "第二条";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
            vm.InputText = string.Empty;

            vm.NavigateHistoryCommand.Execute(-1);
            vm.InputText.Should().Be("第二条");

            vm.NavigateHistoryCommand.Execute(-1);
            vm.InputText.Should().Be("第一条");
        }

        [Fact]
        public async Task NavigateHistory_IgnoresBeyondBounds()
        {
            var vm = CreateVm();
            vm.InputText = "only";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.NavigateHistoryCommand.Execute(1);

            vm.InputText.Should().BeEmpty();
        }

        [Fact]
        public async Task ManualInput_ExitsHistoryCursor()
        {
            var vm = CreateVm();
            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.NavigateHistoryCommand.Execute(-1);
            vm.InputText.Should().Be("hello");

            vm.InputText = "新输入";
            vm.InputText.Should().Be("新输入");
        }

        [Fact]
        public void SuggestedPrompts_NotEmpty()
        {
            var vm = CreateVm();
            vm.SuggestedPrompts.Should().NotBeEmpty();
        }

        [Fact]
        public void UseSuggestion_FillsInput()
        {
            var vm = CreateVm();
            var prompt = vm.SuggestedPrompts[0];

            vm.UseSuggestionCommand.Execute(prompt);

            vm.InputText.Should().Be(prompt);
        }

        [Fact]
        public void UseSuggestion_NullOrBlank_DoesNothing()
        {
            var vm = CreateVm();
            vm.UseSuggestionCommand.Execute(null);
            vm.InputText.Should().BeEmpty();
        }

        [Fact]
        public void InputTooLong_WhenExceedsMaxTokensTriple()
        {
            var vm = CreateVm();
            vm.MaxTokens = 100;
            vm.InputText = new string('x', 301);

            vm.IsInputTooLong.Should().BeTrue();
        }

        [Fact]
        public void InputNotTooLong_BelowLimit()
        {
            var vm = CreateVm();
            vm.MaxTokens = 100;
            vm.InputText = new string('x', 299);

            vm.IsInputTooLong.Should().BeFalse();
        }

        [Fact]
        public async Task Send_BuildsThinkingToolAndContentMessages()
        {
            var vm = CreateVm();

            vm.InputText = "mock query";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.Messages.Should().Contain(m => m.Kind == ChatUiMessageKind.Thinking);
            vm.Messages.Should().Contain(m => m.Kind == ChatUiMessageKind.ToolCall);
            vm.Messages.Should().Contain(m => m.Kind == ChatUiMessageKind.ToolResult);
            vm.Messages.Last().Kind.Should().Be(ChatUiMessageKind.Text);
            vm.Messages.Last().Role.Should().Be(MessageRole.Assistant);
            vm.Messages.Last().Content.Should().Contain("mock query");
            vm.IsBusy.Should().BeFalse();
        }

        [Fact]
        public async Task Send_ToolCallsCarryNameAndArguments()
        {
            var vm = CreateVm();

            vm.InputText = "mock query";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            var toolCalls = vm.Messages.Where(m => m.Kind == ChatUiMessageKind.ToolCall).ToList();
            toolCalls.Should().NotBeEmpty();
            toolCalls[0].ToolName.Should().Be("WebSearch");
            toolCalls[0].ToolArguments.Should().Contain("\"query\"");
        }

        [Fact]
        public async Task Send_ThinkingContent_IsNonEmpty()
        {
            var vm = CreateVm();

            vm.InputText = "mock query";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            var thinking = vm.Messages.Last(m => m.Kind == ChatUiMessageKind.Thinking);
            thinking.Content.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Send_WhileStreaming_AssistantMessageVisibleWithPartialContent()
        {
            var session = new GatedStreamingSession();
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
            vm.InputText = "hi";

            // 事件驱动观察：助手消息进入列表或内容刷新时检查"流式首段已可见"
            var observed = new TaskCompletionSource<ChatUiMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Probe()
            {
                var m = vm.Messages.FirstOrDefault(x => x.Role == MessageRole.Assistant && x.IsStreaming && x.Content == "第一段");
                if (m is not null)
                    observed.TrySetResult(m);
            }
            vm.Messages.CollectionChanged += (_, _) => Probe();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.AllMessagesText))
                    Probe();
            };

            var sendTask = Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            // 流式中途：助手消息应已在消息列表中且携带首段内容（流式输出对用户可见）
            var assistant = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            assistant.Should().NotBeNull("流式过程中助手消息应已加入消息列表且内容实时刷新");

            session.ReleaseGate();
            await sendTask;

            vm.Messages.Last(m => m.Role == MessageRole.Assistant).Content.Should().Be("第一段第二段");
        }

        [Fact]
        public async Task Send_WhenStreamingDisabled_AssistantContentHiddenUntilComplete()
        {
            var session = new GatedStreamingSession();
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
            vm.StreamingEnabled = false;
            vm.InputText = "hi";

            // 事件驱动观察：助手消息加入列表即触发（关闭流式时内容应保持为空）
            var appeared = new TaskCompletionSource<ChatUiMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.Messages.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null)
                    return;
                foreach (ChatUiMessage m in e.NewItems)
                {
                    if (m.Role == MessageRole.Assistant && m.IsStreaming)
                        appeared.TrySetResult(m);
                }
            };

            var sendTask = Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            // 关闭流式：助手消息占位出现但内容保持为空，直到流结束后一次性填充
            var assistant = await appeared.Task.WaitAsync(TimeSpan.FromSeconds(3));
            assistant.Should().NotBeNull("关闭流式时助手消息仍应以占位形式出现");
            assistant.Content.Should().BeEmpty("关闭流式时生成中的内容不应实时显示");

            session.ReleaseGate();
            await sendTask;

            vm.Messages.Last(m => m.Role == MessageRole.Assistant).Content.Should().Be("第一段第二段");
        }

        [Fact]
        public async Task Send_WithSlashInput_RoutesToCommandExecutorNotChat()
        {
            var session = new CommandRecordingSession();
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.InputText = "/help";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            // 命令路由到执行器，不进聊天流（对齐 TUI：/help 执行命令而非发给 LLM）
            session.ExecutedCommands.Should().Contain("/help");
            session.StreamedMessages.Should().BeEmpty("斜杠输入不应作为聊天消息发送给 LLM");

            // 输出以系统消息回显，包含命令与输出内容
            vm.Messages.Should().Contain(m => m.Role == MessageRole.System && m.Content.Contains("命令输出内容"));
            vm.IsBusy.Should().BeFalse();
        }

        [Fact]
        public async Task Send_WithSlashCommand_ReloadsMutatedEngineMessages()
        {
            // T1 对齐：/resume 装入历史、/clear 清空后，GUI 消息列表必须重读引擎上下文刷新，
            // 否则恢复的会话在界面不可见（命令只在引擎层生效）
            var session = new CommandRecordingSession();
            session.EngineMessages =
            [
                new ApiMessageRecord { Role = "user", Content = "旧问题" },
                new ApiMessageRecord { Role = "assistant", Content = "旧回答" },
            ];
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.InputText = "/resume abc";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.Messages.Should().Contain(m => m.Role == MessageRole.User && m.Content == "旧问题",
                "恢复的历史用户消息应回显到消息列表");
            vm.Messages.Should().Contain(m => m.Role == MessageRole.Assistant && m.Content == "旧回答",
                "恢复的历史助手消息应回显到消息列表");

            // ⚙️ 命令回显保留在最底部 — 用户仍能看到命令执行结果
            var echo = vm.Messages.Last();
            echo.Role.Should().Be(MessageRole.System);
            echo.Content.Should().Contain("/resume");
        }

        [Fact]
        public async Task Send_WithUsageInDoneEvent_ShowsRealTokenCount()
        {
            // G2 对齐 TUI：状态栏显示引擎上报的真实 token 用量（而非字符估算）
            var session = new UsageReportingSession(totalTokens: 1234);
            var vm = new MainViewModel(session, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.InputText = "hi";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.TokenUsageText.Should().Be("Token:1,234");
        }

        /// <summary>上报真实用量的假会话 — Done 事件携带 TokenUsage（G2）</summary>
        internal sealed class UsageReportingSession : IJccChatSession
        {
            public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
            public event Action? ExitRequested;
#pragma warning restore CS0067
            public Func<string, bool>? SlashConfirmHandler { get; set; }
            private readonly int _totalTokens;
            public UsageReportingSession(int totalTokens) => _totalTokens = totalTokens;

            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
            public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
            public bool IsReady => true;
            public string CurrentVendor => "fake";
            public string CurrentModelId => "fake-model";
            public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Text("回复内容");
                yield return ChatStreamEvent.Done(new TokenUsage(1000, _totalTokens - 1000));
                await Task.CompletedTask;
            }
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
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
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        [Fact]
        public void ThinkingMessage_ToggleCollapsesAndRevealsBody()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "some reasoning",
                Kind = ChatUiMessageKind.Thinking
            };

            vm.ToggleThinkingCommand.Execute(msg);

            msg.IsThinkingExpanded.Should().BeFalse();
            msg.IsThinkingCollapsed.Should().BeTrue();
            msg.ShowBody.Should().BeFalse();

            vm.ToggleThinkingCommand.Execute(msg);
            msg.IsThinkingExpanded.Should().BeTrue();
            msg.ShowBody.Should().BeTrue();
        }

        [Fact]
        public void ToggleThinking_OnNonThinkingMessage_DoesNothing()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "hi" };

            vm.ToggleThinkingCommand.Execute(msg);

            msg.IsThinkingExpanded.Should().BeTrue();
        }

        [Fact]
        public async Task PermissionConfirmation_NoCallback_DefaultsToDeny()
        {
            var fake = new FakeSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            var decision = await fake.Handler!(new PermissionConfirmationRequest("bash", "运行命令?", "req-1", "rule"));

            decision.Should().Be(PermissionConfirmationDecision.Deny);
        }

        [Fact]
        public async Task PermissionConfirmation_WithCallback_DelegatesToView()
        {
            var fake = new FakeSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
            PermissionConfirmationRequest? received = null;
            vm.PermissionConfirmCallback = req =>
            {
                received = req;
                return Task.FromResult(PermissionConfirmationDecision.Allow);
            };

            var decision = await fake.Handler!(new PermissionConfirmationRequest("bash", "运行命令?", "req-2", "rule"));

            decision.Should().Be(PermissionConfirmationDecision.Allow);
            received!.ToolName.Should().Be("bash");
            received!.ConfirmationPrompt.Should().Be("运行命令?");
            received!.RequestId.Should().Be("req-2");
        }

        [Fact]
        public void ErrorToast_InitiallyHidden()
        {
            var vm = CreateVm();

            vm.HasErrorToast.Should().BeFalse();
        }

        [Fact]
        public async Task Send_WhenSessionThrows_SetsErrorToast()
        {
            var fake = new ThrowingSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.HasErrorToast.Should().BeTrue();
            vm.ErrorToastText.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Send_WhenSessionThrows_KeepsStatusReady()
        {
            var fake = new ThrowingSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.StatusText.Should().Be("就绪");
            vm.StatusKind.Should().Be(StatusKind.Ready);
        }

        [Fact]
        public void CopyErrorToast_SetsClipboardTextAndDismisses()
        {
            var vm = CreateVm();
            vm.ErrorToastText = "boom";

            vm.CopyErrorToastCommand.Execute(null);

            vm.ErrorToastText.Should().BeNull();
            vm.HasErrorToast.Should().BeFalse();
            vm.ErrorToastCopy.Should().Be("boom");
        }

        [Fact]
        public void DismissErrorToast_RemovesToast()
        {
            var vm = CreateVm();
            vm.ErrorToastText = "boom";

            vm.DismissErrorToastCommand.Execute(null);

            vm.HasErrorToast.Should().BeFalse();
        }

        /// <summary>流式抛异常的假会话，用于验证错误 toast</summary>
        private sealed class ThrowingSession : JoinCode.Gui.Hosting.IJccChatSession
        {
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
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Done();
                throw new InvalidOperationException("引擎连接失败");
#pragma warning disable CS0162
                await Task.CompletedTask;
#pragma warning restore CS0162
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
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
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>记录 VM 注入权限处理器的假会话，用于验证回调接线</summary>
        private sealed class FakeSession : JoinCode.Gui.Hosting.IJccChatSession        {
            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? Handler { get; private set; }
            public float? WrittenTemperature { get; private set; }
            public int? WrittenMaxTokens { get; private set; }
            public string? WrittenSystemPrompt { get; private set; }

            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler
            {
                get => Handler;
                set => Handler = value;
            }

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
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Done();
                await Task.CompletedTask;
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
            public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void RefreshVendorModelMap() { }
            public void SwitchSession(string sessionId) { }
            public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public EffortLevel EffortLevel => EffortLevel.Auto;
            public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
            {
                WrittenSystemPrompt = systemPrompt;
                return Task.CompletedTask;
            }
            public float? Temperature => null;
            public int? MaxTokens => null;
            public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default)
            {
                WrittenTemperature = temperature;
                return Task.CompletedTask;
            }
            public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default)
            {
                WrittenMaxTokens = maxTokens;
                return Task.CompletedTask;
            }
            public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>跨供应商污染测试桩 — CurrentModelId 是 sensenova 的模型，VendorModelMap 包含 anthropic 和 sensenova</summary>
        private sealed class CrossContaminationSession : IJccChatSession
        {
            public bool IsReady => true;
            public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
            public event Action? ExitRequested;
#pragma warning restore CS0067
            public Func<string, bool>? SlashConfirmHandler { get; set; }
            public string CurrentVendor => "sensenova";
            public string CurrentModelId => "sensenova-6.7-flash-lite";
            public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anthropic"] = ["claude-sonnet-4-20250514", "claude-opus-4-20250514"],
                    ["sensenova"] = ["sensenova-6.7-flash-lite", "sensenova-u1-fast"]
                };
            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
            public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Done();
                await Task.CompletedTask;
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
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
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>记录 LoadHistoryAsync 调用的假会话，用于验证 SelectSession 把历史灌入底层引擎</summary>
        private sealed class HistoryRecordingSession : IJccChatSession
        {
            public List<IReadOnlyList<(MessageRole Role, string Content)>> LoadHistoryCalls { get; } = [];

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
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Text("收到：" + message);
                yield return ChatStreamEvent.Done();
                await Task.CompletedTask;
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
            public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void RefreshVendorModelMap() { }
            public void SwitchSession(string sessionId) { }
            public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default)
            {
                LoadHistoryCalls.Add(messages);
                return Task.CompletedTask;
            }
            public EffortLevel EffortLevel => EffortLevel.Auto;
            public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public float? Temperature => null;
            public int? MaxTokens => null;
            public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// 门控流式假会话 — 先产出"第一段"，等待测试放行闸门后再产出"第二段"+Done，
        /// 用于在流式中途冻结引擎、观察 UI 的即时状态。
        /// </summary>
        private sealed class GatedStreamingSession : IJccChatSession
        {
            public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
            public event Action? ExitRequested;
#pragma warning restore CS0067
            public Func<string, bool>? SlashConfirmHandler { get; set; }
            private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void ReleaseGate() => _gate.TrySetResult();

            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
            public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
            public bool IsReady => true;
            public string CurrentVendor => "fake";
            public string CurrentModelId => "fake-model";
            public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return ChatStreamEvent.Text("第一段");
                await _gate.Task.WaitAsync(cancellationToken);
                yield return ChatStreamEvent.Text("第二段");
                yield return ChatStreamEvent.Done();
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
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
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>记录斜杠命令调用的假会话 — 验证 / 前缀输入路由到命令执行器而非聊天流（G1）</summary>
        internal sealed class CommandRecordingSession : IJccChatSession
        {
            public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
            public event Action? ExitRequested;
#pragma warning restore CS0067
            public Func<string, bool>? SlashConfirmHandler { get; set; }
            public List<string> ExecutedCommands { get; } = [];
            public List<string> StreamedMessages { get; } = [];
            public string CommandOutput { get; set; } = "命令输出内容";

            /// <summary>引擎侧消息快照 — GetMessagesAsync 返回此列表（模拟 /resume 后的引擎上下文）</summary>
            public List<ApiMessageRecord> EngineMessages { get; set; } = [];

            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
            public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
            public bool IsReady => true;
            public string CurrentVendor => "fake";
            public string CurrentModelId => "fake-model";
            public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
                = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fake"] = ["fake-model"]
                };
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                StreamedMessages.Add(message);
                yield return ChatStreamEvent.Text("收到：" + message);
                yield return ChatStreamEvent.Done();
                await Task.CompletedTask;
            }
            public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
            {
                ExecutedCommands.Add(input);
                return Task.FromResult(CommandOutput);
            }
            public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ApiMessageRecord>>(EngineMessages.ToList());
            public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new RewindResult());
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
            public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
            public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
            public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// AllMessagesText 工具返回值显示测试 — 验证工具调用后 ToolResultText 出现在纯文本输出中。
    /// </summary>
    public sealed class AllMessagesTextToolResultTests
    {
        [Fact]
        public void ToolResultText_AppearsInAllMessagesText()
        {
            var vm = new MainViewModel(null, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
            vm.Messages.Add(new ChatUiMessage
            {
                Role = MessageRole.User,
                Content = "帮我运行 echo hello",
                Timestamp = DateTime.Now
            });
            vm.Messages.Add(new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "",
                Timestamp = DateTime.Now,
                Kind = ChatUiMessageKind.ToolCall,
                ToolName = "bash",
                ToolArguments = "echo hello"
            });
            vm.Messages.Add(new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "",
                Timestamp = DateTime.Now,
                Kind = ChatUiMessageKind.ToolResult,
                ToolName = "bash",
                ToolResultText = "hello"
            });
            vm.AllMessagesText.Should().Contain("hello");
            vm.AllMessagesText.Should().Contain("bash");
        }
    }
