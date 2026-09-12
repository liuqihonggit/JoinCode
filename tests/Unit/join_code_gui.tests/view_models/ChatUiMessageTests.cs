namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// ChatUiMessage 单元测试 — 验证 UI 消息模型的角色标签、展示类型、思考折叠、
/// 工具状态、代码块检测、CopyAllText 纯文本导出与 INPC 通知。
/// 这些行为直接驱动 XAML 绑定可见性与终端式复制输出。
/// </summary>
public sealed class ChatUiMessageTests
{
    private static ChatUiMessage Msg(MessageRole role = MessageRole.Assistant, string content = "", ChatUiMessageKind kind = ChatUiMessageKind.Text)
        => new() { Role = role, Content = content, Kind = kind, Timestamp = new DateTime(2026, 1, 1, 10, 0, 0) };

    // ── 角色标签 ──

    [Fact]
    public void RoleLabel_User_IsYou()
    {
        var msg = Msg(MessageRole.User);
        msg.RoleLabel.Should().Be("你");
    }

    [Fact]
    public void RoleLabel_Assistant_IsAi()
    {
        var msg = Msg(MessageRole.Assistant);
        msg.RoleLabel.Should().Be("AI");
    }

    [Fact]
    public void RoleLabel_System_IsSystem()
    {
        var msg = Msg(MessageRole.System);
        msg.RoleLabel.Should().Be("系统");
    }

    [Fact]
    public void IsUser_True_OnlyForUserRole()
    {
        Msg(MessageRole.User).IsUser.Should().BeTrue();
        Msg(MessageRole.Assistant).IsUser.Should().BeFalse();
        Msg(MessageRole.System).IsUser.Should().BeFalse();
    }

    // ── 类型标签 ──

    [Fact]
    public void KindLabel_Thinking_HasBrainEmoji()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking);
        msg.KindLabel.Should().Contain("思考");
    }

    [Fact]
    public void KindLabel_ToolCall_HasWrenchEmoji()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolCall);
        msg.KindLabel.Should().Contain("工具调用");
    }

    [Fact]
    public void KindLabel_ToolResult_HasCheckEmoji()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.KindLabel.Should().Contain("工具结果");
    }

    [Fact]
    public void KindLabel_Text_IsEmpty()
    {
        var msg = Msg(kind: ChatUiMessageKind.Text);
        msg.KindLabel.Should().BeEmpty();
    }

    // ── 类型判定属性 ──

    [Fact]
    public void IsToolCall_True_ForToolCallAndToolResult()
    {
        Msg(kind: ChatUiMessageKind.ToolCall).IsToolCall.Should().BeTrue();
        Msg(kind: ChatUiMessageKind.ToolResult).IsToolCall.Should().BeTrue();
        Msg(kind: ChatUiMessageKind.Text).IsToolCall.Should().BeFalse();
        Msg(kind: ChatUiMessageKind.Thinking).IsToolCall.Should().BeFalse();
    }

    [Fact]
    public void IsToolCallStart_True_OnlyForToolCall()
    {
        Msg(kind: ChatUiMessageKind.ToolCall).IsToolCallStart.Should().BeTrue();
        Msg(kind: ChatUiMessageKind.ToolResult).IsToolCallStart.Should().BeFalse();
    }

    [Fact]
    public void IsToolResultMessage_True_OnlyForToolResult()
    {
        Msg(kind: ChatUiMessageKind.ToolResult).IsToolResultMessage.Should().BeTrue();
        Msg(kind: ChatUiMessageKind.ToolCall).IsToolResultMessage.Should().BeFalse();
    }

    [Fact]
    public void IsThinking_True_OnlyForThinkingKind()
    {
        Msg(kind: ChatUiMessageKind.Thinking).IsThinking.Should().BeTrue();
        Msg(kind: ChatUiMessageKind.Text).IsThinking.Should().BeFalse();
    }

    // ── HasDiff ──

    [Fact]
    public void HasDiff_False_WhenStructuredPatchNull()
    {
        var msg = Msg();
        msg.StructuredPatch.Should().BeNull();
        msg.HasDiff.Should().BeFalse();
    }

    [Fact]
    public void HasDiff_False_WhenStructuredPatchEmpty()
    {
        var msg = Msg();
        msg.StructuredPatch = [];
        msg.HasDiff.Should().BeFalse();
    }

    [Fact]
    public void HasDiff_True_WhenStructuredPatchNonEmpty()
    {
        var msg = Msg();
        msg.StructuredPatch = [new StructuredPatchHunk { OldStart = 1, OldLines = 1, NewStart = 1, NewLines = 1, Header = "@@ -1 +1 @@", Lines = [] }];
        msg.HasDiff.Should().BeTrue();
    }

    // ── DisplayText ──

    [Fact]
    public void DisplayText_ToolResult_PrefersToolResultText()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.Content = "正文";
        msg.ToolResultText = "结果文本";

        msg.DisplayText.Should().Be("结果文本");
    }

    [Fact]
    public void DisplayText_NonToolResult_PrefersContent()
    {
        var msg = Msg(content: "正文");
        msg.DisplayText.Should().Be("正文");
    }

    [Fact]
    public void DisplayText_ToolResult_WithNullToolResultText_IsEmpty()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.ToolResultText = null;

        msg.DisplayText.Should().BeEmpty();
    }

    [Fact]
    public void DisplayText_ToolError_ParsesToolUseErrorTag()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.ToolResultText = "<tool_use_error>参数类型不匹配：期望数组，实际为字符串</tool_use_error>";
        msg.IsToolError = true;

        msg.DisplayText.Should().Be("参数类型不匹配：期望数组，实际为字符串");
    }

    [Fact]
    public void DisplayText_ToolError_NoTag_ReturnsOriginalText()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.ToolResultText = "执行失败：文件不存在";
        msg.IsToolError = true;

        msg.DisplayText.Should().Be("执行失败：文件不存在");
    }

    [Fact]
    public void DisplayText_ToolSuccess_DoesNotParseErrorTag()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolResult);
        msg.ToolResultText = "<tool_use_error>不应解析</tool_use_error>";
        msg.IsToolError = false;

        msg.DisplayText.Should().Be("<tool_use_error>不应解析</tool_use_error>");
    }

    [Fact]
    public void CopyAllText_ToolError_ParsesToolUseErrorTag()
    {
        var msg = Msg(MessageRole.Assistant, string.Empty, ChatUiMessageKind.ToolResult);
        msg.ToolName = "read_file";
        msg.ToolResultText = "<tool_use_error>文件过大，超过 10MB 限制</tool_use_error>";
        msg.IsToolError = true;

        var copyText = msg.CopyAllText;
        copyText.Should().Contain("文件过大，超过 10MB 限制");
        copyText.Should().NotContain("<tool_use_error>");
    }

    // ── IsCodeBlock ──

    [Fact]
    public void CodeBlock_DetectsFencedBlock()
    {
        var msg = Msg(MessageRole.Assistant, "```csharp\nint x = 1;\n```");
        msg.IsCodeBlock.Should().BeTrue();
    }

    [Fact]
    public void CodeBlock_DetectsUsingSystem()
    {
        var msg = Msg(MessageRole.Assistant, "using System;\nclass Foo {}");
        msg.IsCodeBlock.Should().BeTrue();
    }

    [Fact]
    public void CodeBlock_DetectsInclude()
    {
        var msg = Msg(MessageRole.Assistant, "#include <stdio.h>");
        msg.IsCodeBlock.Should().BeTrue();
    }

    [Fact]
    public void CodeBlock_PlainText_IsFalse()
    {
        var msg = Msg(MessageRole.Assistant, "这是一个普通回答。");
        msg.IsCodeBlock.Should().BeFalse();
    }

    [Fact]
    public void CodeBlock_LeadingWhitespace_StillDetected()
    {
        var msg = Msg(MessageRole.Assistant, "  ```python\nprint(1)\n```");
        msg.IsCodeBlock.Should().BeTrue();
    }

    // ── 思考折叠 ──

    [Fact]
    public void ThinkingMessage_DefaultsToExpanded()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking, content: "some reasoning");

        msg.IsThinkingExpanded.Should().BeTrue();
        msg.IsThinkingCollapsed.Should().BeFalse();
        msg.ShowBody.Should().BeTrue();
    }

    [Fact]
    public void ThinkingMessage_Collapse_HidesBody()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking, content: "some reasoning");

        msg.IsThinkingExpanded = false;

        msg.IsThinkingExpanded.Should().BeFalse();
        msg.IsThinkingCollapsed.Should().BeTrue();
        msg.ShowBody.Should().BeFalse();
    }

    [Fact]
    public void ThinkingMessage_ReExpand_ShowsBody()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking, content: "some reasoning");
        msg.IsThinkingExpanded = false;

        msg.IsThinkingExpanded = true;

        msg.ShowBody.Should().BeTrue();
    }

    [Fact]
    public void ThinkingSummary_WhenCollapsed_ShowsLengthHint()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking, content: "abc");
        msg.IsThinkingExpanded = false;

        msg.ThinkingSummary.Should().Contain("3");
        msg.ThinkingSummary.Should().Contain("展开");
    }

    [Fact]
    public void ThinkingSummary_NonThinkingMessage_IsEmpty()
    {
        var msg = Msg(kind: ChatUiMessageKind.Text, content: "abc");
        msg.ThinkingSummary.Should().BeEmpty();
    }

    [Fact]
    public void ShowBody_TextMessage_AlwaysTrue()
    {
        var msg = Msg(kind: ChatUiMessageKind.Text);
        msg.ShowBody.Should().BeTrue();
    }

    [Fact]
    public void ShowBody_ToolCallMessage_False()
    {
        var msg = Msg(kind: ChatUiMessageKind.ToolCall);
        msg.ShowBody.Should().BeFalse();
    }

    // ── INPC 通知 ──

    [Fact]
    public void Content_SetDifferentValue_RaisesContentAndThinkingSummary()
    {
        var msg = Msg(content: "old");
        var fired = new List<string?>();
        ((INotifyPropertyChanged)msg).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        msg.Content = "new";

        fired.Should().Contain(nameof(ChatUiMessage.Content));
        fired.Should().Contain(nameof(ChatUiMessage.ThinkingSummary));
    }

    [Fact]
    public void Content_SetSameValue_DoesNotRaise()
    {
        var msg = Msg(content: "same");
        var fired = new List<string?>();
        ((INotifyPropertyChanged)msg).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        msg.Content = "same";

        fired.Should().BeEmpty();
    }

    [Fact]
    public void IsToolRunning_Toggle_RaisesPropertyChanged()
    {
        var msg = Msg();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)msg).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        msg.IsToolRunning = true;

        fired.Should().Contain(nameof(ChatUiMessage.IsToolRunning));
    }

    [Fact]
    public void ToolElapsedText_SetDifferentValue_RaisesPropertyChanged()
    {
        var msg = Msg();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)msg).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        msg.ToolElapsedText = "⏱ 1.5s";

        fired.Should().Contain(nameof(ChatUiMessage.ToolElapsedText));
    }

    [Fact]
    public void IsThinkingExpanded_Toggle_RaisesBothExpandedAndCollapsed()
    {
        var msg = Msg(kind: ChatUiMessageKind.Thinking);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)msg).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        msg.IsThinkingExpanded = false;

        fired.Should().Contain(nameof(ChatUiMessage.IsThinkingExpanded));
        fired.Should().Contain(nameof(ChatUiMessage.IsThinkingCollapsed));
    }

    // ── RefreshElapsed ──

    [Fact]
    public void RefreshElapsed_WithToolStartTime_UpdatesToolElapsedText()
    {
        var msg = Msg();
        msg.ToolStartTime = DateTime.Now.AddSeconds(-2);

        msg.RefreshElapsed();

        msg.ToolElapsedText.Should().StartWith("⏱ ");
        msg.ToolElapsedText.Should().Contain("s");
    }

    [Fact]
    public void RefreshElapsed_WithNullToolStartTime_DoesNothing()
    {
        var msg = Msg();
        msg.ToolStartTime = null;

        msg.RefreshElapsed();

        msg.ToolElapsedText.Should().BeEmpty();
    }

    [Fact]
    public void RefreshElapsed_Over60Seconds_ShowsMinutesFormat()
    {
        var msg = Msg();
        msg.ToolStartTime = DateTime.Now.AddSeconds(-125);

        msg.RefreshElapsed();

        msg.ToolElapsedText.Should().Contain(":");
    }

    // ── CopyAllText ──

    [Fact]
    public void CopyAllText_IncludesRoleLabelAndContent()
    {
        var msg = Msg(MessageRole.Assistant, "hello");

        var text = msg.CopyAllText;

        text.Should().Contain("AI");
        text.Should().Contain("hello");
        text.Should().Contain("10:00:00");
    }

    [Fact]
    public void CopyAllText_ThinkingIncludesThoughtContent()
    {
        var msg = Msg(MessageRole.Assistant, "先分析再动手", ChatUiMessageKind.Thinking);

        var text = msg.CopyAllText;

        text.Should().Contain("思考");
        text.Should().Contain("先分析再动手");
    }

    [Fact]
    public void CopyAllText_ToolResult_IncludesToolAndDiff()
    {
        var msg = Msg(MessageRole.Assistant, string.Empty, ChatUiMessageKind.ToolResult);
        msg.ToolName = "edit_file";
        msg.ToolResultText = "修改完成";
        msg.StructuredPatch =
        [
            new StructuredPatchHunk
            {
                OldStart = 1,
                OldLines = 1,
                NewStart = 1,
                NewLines = 1,
                Header = "@@ -1 +1 @@",
                Lines =
                [
                    new PatchLine { Type = PatchLineType.Removed, Content = "old", OldLineNumber = 1 },
                    new PatchLine { Type = PatchLineType.Added, Content = "new", NewLineNumber = 1 },
                ],
            },
        ];

        var text = msg.CopyAllText;

        text.Should().Contain("edit_file");
        text.Should().Contain("修改完成");
        text.Should().Contain("-old");
        text.Should().Contain("+new");
        text.Should().Contain("@@ -1 +1 @@");
    }

    [Fact]
    public void CopyAllText_EmptyMessage_IsEmpty()
    {
        var msg = Msg(MessageRole.User, string.Empty);

        msg.CopyAllText.Should().BeEmpty();
    }

    [Fact]
    public void CopyAllText_ToolCallStart_IncludesArguments()
    {
        var msg = Msg(MessageRole.Assistant, string.Empty, ChatUiMessageKind.ToolCall);
        msg.ToolName = "read_file";
        msg.ToolArguments = "{\"path\":\"foo.cs\"}";

        var text = msg.CopyAllText;

        text.Should().Contain("read_file");
        text.Should().Contain("\"path\"");
    }

    [Fact]
    public void CopyAllText_WhitespaceContent_IsEmpty()
    {
        var msg = Msg(MessageRole.Assistant, "   \n  ");

        msg.CopyAllText.Should().BeEmpty();
    }
}
