using FluentAssertions;

using IO.FileSystem;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Gui.Hosting;
using JoinCode.Gui.Persistence;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// MainViewModel 冒烟测试 — 验证"输入→回显→角色化消息"命令链路（不依赖真实引擎）。
/// 异步命令需在 Task.Run 下执行并对命令调用 WaitAsync(Timeout) 施加硬超时，防止在
/// xUnit 单线程同步上下文或 IAsyncEnumerable 续体调度下可能出现的死锁，保证任何情况都能在 5 秒内结束测试并退出。
/// </summary>
public class MainViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 避免测试污染真实 ~/.jcc/sessions</summary>
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"));

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
    public void RemoveSession_DeletesPersistedFile()
    {
        var store = new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions");
        var vm = new MainViewModel(null, store);

        vm.InputText = "hello";
        vm.SendCommand.Execute(null);
        var target = vm.Sessions.First(s => s.IsSelected);

        vm.RemoveSessionCommand.Execute(target);

        store.Load(target.Id).Should().BeNull();
        vm.Sessions.Should().NotContain(target);
    }

    [Fact]
    public void ModelOptions_AreBoundToSessionRealModels()
    {
        var vm = CreateVm();

        vm.ModelOptions.Select(m => m.Id).Should().BeEquivalentTo(["deepseek-chat", "deepseek-reasoner"]);
        vm.ModelOptions.Should().Contain(m => m.Id == "deepseek-chat" && m.DisplayText == "DeepSeek · deepseek-chat");
        vm.SelectedModel.Should().Be("deepseek-chat");
        vm.SelectedModelOption.Should().NotBeNull();
        vm.SelectedModelOption!.Id.Should().Be("deepseek-chat");
    }

    [Fact]
    public void ModelOptions_DisplayText_DistinguishesProviderAndModel()
    {
        var vm = CreateVm();

        foreach (var item in vm.ModelOptions)
        {
            item.DisplayText.Should().Contain("·", "展示文本应区分供应商与模型：如 'DeepSeek · deepseek-chat'");
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
        var vm = CreateVm();

        var target = vm.ModelOptions.First(m => m.Id == "deepseek-reasoner");
        vm.SelectedModelOption = target;

        vm.SelectedModel.Should().Be("deepseek-reasoner");
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
        public void CodeBlock_DetectsFencedBlock()
        {
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "```csharp\nint x = 1;\n```" };
            msg.IsCodeBlock.Should().BeTrue();
        }

        [Fact]
        public void CodeBlock_DetectsUsingSystem()
        {
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "using System;\nclass Foo {}" };
            msg.IsCodeBlock.Should().BeTrue();
        }

        [Fact]
        public void CodeBlock_PlainText_IsFalse()
        {
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "这是一个普通回答。" };
            msg.IsCodeBlock.Should().BeFalse();
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
        public void ThinkingMessage_DefaultsToCollapsed()
        {
            var msg = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "some reasoning",
                Kind = ChatUiMessageKind.Thinking
            };

            msg.IsThinkingExpanded.Should().BeFalse();
            msg.IsThinkingCollapsed.Should().BeTrue();
            msg.ShowBody.Should().BeFalse();
        }

        [Fact]
        public void ThinkingMessage_ToggleExpandsAndRevealsBody()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "some reasoning",
                Kind = ChatUiMessageKind.Thinking
            };

            vm.ToggleThinkingCommand.Execute(msg);

            msg.IsThinkingExpanded.Should().BeTrue();
            msg.IsThinkingCollapsed.Should().BeFalse();
            msg.ShowBody.Should().BeTrue();

            vm.ToggleThinkingCommand.Execute(msg);
            msg.IsThinkingExpanded.Should().BeFalse();
        }

        [Fact]
        public void ToggleThinking_OnNonThinkingMessage_DoesNothing()
        {
            var vm = CreateVm();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "hi" };

            vm.ToggleThinkingCommand.Execute(msg);

            msg.IsThinkingExpanded.Should().BeFalse();
        }

        [Fact]
        public void ThinkingSummary_WhenCollapsed_ShowsLengthHint()
        {
            var msg = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = "abc",
                Kind = ChatUiMessageKind.Thinking
            };

            msg.ThinkingSummary.Should().Contain("3");
            msg.ThinkingSummary.Should().Contain("展开");
        }

        [Fact]
        public async Task PermissionConfirmation_NoCallback_DefaultsToDeny()
        {
            var fake = new FakeSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"));

            var decision = await fake.Handler!(new PermissionConfirmationRequest("bash", "运行命令?", "req-1", "rule"));

            decision.Should().Be(PermissionConfirmationDecision.Deny);
        }

        [Fact]
        public async Task PermissionConfirmation_WithCallback_DelegatesToView()
        {
            var fake = new FakeSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"));
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
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"));

            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.HasErrorToast.Should().BeTrue();
            vm.ErrorToastText.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Send_WhenSessionThrows_KeepsStatusReady()
        {
            var fake = new ThrowingSession();
            var vm = new MainViewModel(fake, new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"));

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

            public bool IsReady => true;
            public string CurrentProvider => "fake";
            public string CurrentModelId => "fake-model";
            public IReadOnlyList<string> AvailableModels => ["fake-model"];
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            public EffortLevel EffortLevel => EffortLevel.Auto;
            public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>记录 VM 注入权限处理器的假会话，用于验证回调接线</summary>
        private sealed class FakeSession : JoinCode.Gui.Hosting.IJccChatSession        {
            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? Handler { get; private set; }

            public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler
            {
                get => Handler;
                set => Handler = value;
            }

            public bool IsReady => true;
            public string CurrentProvider => "fake";
            public string CurrentModelId => "fake-model";
            public IReadOnlyList<string> AvailableModels => ["fake-model"];
            public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            public EffortLevel EffortLevel => EffortLevel.Auto;
            public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }