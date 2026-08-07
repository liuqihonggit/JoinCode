using FluentAssertions;

using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Gui.Hosting;
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

    [Fact]
    public async Task Send_WithValidInput_BuildsUserAndAssistantMessages()
    {
        var vm = new MainViewModel();

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
        var vm = new MainViewModel();

        vm.InputText = "   ";
        await vm.SendCommand.ExecuteAsync(null).WaitAsync(Timeout);

        vm.Messages.Should().BeEmpty();
        vm.InputText.Should().Be("   ");
    }

    [Fact]
    public async Task ClearHistory_RemovesMessages()
    {
        var vm = new MainViewModel();

        vm.InputText = "hello";
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        vm.Messages.Should().NotBeEmpty();

        await vm.ClearHistoryCommand.ExecuteAsync(null).WaitAsync(Timeout);
        vm.Messages.Should().BeEmpty();
    }

    [Fact]
    public void NewConversation_CreatesSessionAndClearsMessages()
    {
        var vm = new MainViewModel();
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
    public async Task RemoveMessage_DeletesSingleMessage()
    {
        var vm = new MainViewModel();

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
        var vm = new MainViewModel();

        vm.InsertDividerCommand.Execute(null);
        vm.InputText.Should().Contain("---");

        vm.InsertTimestampCommand.Execute(null);
        vm.InputText.Should().Contain(":");
    }

    [Fact]
    public void InputTextChange_UpdatesCharsCount()
    {
        var vm = new MainViewModel();
        vm.InputText = "abcde";
        vm.CharsCount.Should().Be(5);
    }

[Fact]
        public void RemoveSession_RemovesFromList()
        {
            var vm = new MainViewModel();
            var before = vm.Sessions.Count;
            var target = vm.Sessions[^1];

            vm.RemoveSessionCommand.Execute(target);

            vm.Sessions.Should().HaveCount(before - 1);
            vm.Sessions.Should().NotContain(target);
        }

        [Fact]
        public void SelectSession_MarksOnlyTargetSelected()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            vm.InputText = "帮我写个爬虫脚本";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            var active = vm.SelectedSession;
            active!.Title.Should().Be("帮我写个爬虫脚本");
        }

        [Fact]
        public void CopyMessage_SetsFeedbackState()
        {
            var vm = new MainViewModel();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "sample" };
            vm.Messages.Add(msg);

            vm.CopyMessageCommand.Execute(msg);

            vm.HasCopied.Should().BeTrue();
        }

        [Fact]
        public void CopyEmptyMessage_DoesNotSetFeedback()
        {
            var vm = new MainViewModel();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = string.Empty };
            vm.Messages.Add(msg);

            vm.CopyMessageCommand.Execute(msg);

            vm.HasCopied.Should().BeFalse();
        }

        [Fact]
        public void BeginRename_PutsSessionIntoEditState()
        {
            var vm = new MainViewModel();
            var session = vm.Sessions[0];

            vm.BeginRenameSessionCommand.Execute(session);

            session.IsRenaming.Should().BeTrue();
            session.IsSelected.Should().BeTrue();
            session.RenameDraft.Should().Be(session.Title);
        }

        [Fact]
        public void CommitRename_AppliesDraftTitle()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();

            vm.StopGeneratingCommand.Execute(null);
            vm.CanStop.Should().BeFalse();
            vm.IsBusy.Should().BeFalse();
        }

        [Fact]
        public void StatusKind_ErrorPrefix_MapsToError()
        {
            var vm = new MainViewModel();
            vm.StatusText = "错误: something failed";
            vm.StatusKind.Should().Be(StatusKind.Error);
        }

        [Fact]
        public void StatusKind_Thinking_MapsToBusy()
        {
            var vm = new MainViewModel();
            vm.StatusText = "思考中…";
            vm.StatusKind.Should().Be(StatusKind.Busy);
        }

        [Fact]
        public void StatusKind_Ready_MapsToReady()
        {
            var vm = new MainViewModel();
            vm.StatusText = "就绪";
            vm.StatusKind.Should().Be(StatusKind.Ready);
        }

        [Fact]
        public void ClearAllSessions_ResetsToListWithOneSession()
        {
            var vm = new MainViewModel();
            vm.SendCommand.Execute(null);

            vm.ClearAllSessionsCommand.Execute(null);

            vm.Sessions.Should().HaveCount(1);
            vm.Messages.Should().BeEmpty();
        }

        [Fact]
        public void SystemPrompt_HasDefaultValue()
        {
            var vm = new MainViewModel();
            vm.SystemPrompt.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task CanRegenerate_AfterReply_IsTrue()
        {
            var vm = new MainViewModel();
            vm.InputText = "hello";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.CanRegenerate.Should().BeTrue();
        }

        [Fact]
        public async Task Regenerate_RemovesLastTurnAndResends()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();

            var act = () => vm.RegenerateLastReplyCommand.Execute(null);

            act.Should().NotThrow();
            vm.Messages.Should().BeEmpty();
        }

        [Fact]
        public void EstimatedTokens_TracksMessageContent()
        {
            var vm = new MainViewModel();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "abcdefgh" });

            vm.EstimatedTokens.Should().Be(2);
            vm.TotalChars.Should().Be(8);
        }

        [Fact]
        public void ResetSettings_RestoresDefaults()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            vm.FontSize.Should().Be(14);
        }

        [Fact]
        public void FilteredMessages_EmptySearch_ReturnsAll()
        {
            var vm = new MainViewModel();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "苹果" });
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "香蕉" });

            vm.FilteredMessages.Should().HaveCount(2);
        }

        [Fact]
        public void FilteredMessages_SearchFiltersByKeyword()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "Hello World" };
            vm.Messages.Add(msg);

            vm.SearchText = "world";

            vm.FilteredMessages.Should().Contain(msg);
        }

        [Fact]
        public void ExportSessionText_ContainsRolesAndContents()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "hi" });

            vm.CopySessionExportCommand.Execute(null);

            vm.ExportedSessionCopy.Should().Contain("hi");
        }

        [Fact]
        public async Task NavigateHistory_TraversesSentMessages()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            vm.InputText = "only";
            await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);

            vm.NavigateHistoryCommand.Execute(1);

            vm.InputText.Should().BeEmpty();
        }

        [Fact]
        public async Task ManualInput_ExitsHistoryCursor()
        {
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
            vm.SuggestedPrompts.Should().NotBeEmpty();
        }

        [Fact]
        public void UseSuggestion_FillsInput()
        {
            var vm = new MainViewModel();
            var prompt = vm.SuggestedPrompts[0];

            vm.UseSuggestionCommand.Execute(prompt);

            vm.InputText.Should().Be(prompt);
        }

        [Fact]
        public void UseSuggestion_NullOrBlank_DoesNothing()
        {
            var vm = new MainViewModel();
            vm.UseSuggestionCommand.Execute(null);
            vm.InputText.Should().BeEmpty();
        }

        [Fact]
        public void InputTooLong_WhenExceedsMaxTokensTriple()
        {
            var vm = new MainViewModel();
            vm.MaxTokens = 100;
            vm.InputText = new string('x', 301);

            vm.IsInputTooLong.Should().BeTrue();
        }

        [Fact]
        public void InputNotTooLong_BelowLimit()
        {
            var vm = new MainViewModel();
            vm.MaxTokens = 100;
            vm.InputText = new string('x', 299);

            vm.IsInputTooLong.Should().BeFalse();
        }

        [Fact]
        public async Task Send_BuildsThinkingToolAndContentMessages()
        {
            var vm = new MainViewModel();

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
            var vm = new MainViewModel();

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
            var vm = new MainViewModel();

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
            var vm = new MainViewModel();
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
            var vm = new MainViewModel();
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
    }