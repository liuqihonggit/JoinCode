namespace NativeTui.Tests.Repl;

/// <summary>
/// 键位绑定表测试 — 默认表转录正确性抽查（以基准 defaultBindings.ts 实际读到的键位为准）+
/// Global 键级回退 + 单键反查/序列查询语义（对齐基准 resolver.ts）。
/// </summary>
public sealed class KeybindingProfileTests
{
    /// <summary>构造按键辅助（测试可读性）。</summary>
    private static KeyPress Ctrl(char letter) => new(KeyKind.Char, letter.ToString(), Ctrl: true);

    /// <summary>Resolve 结果的键位串集合（断言用 — 不依赖声明序）。</summary>
    private static string[] KeysOf(KeybindingProfile profile, KeybindingContext context, KeybindingAction action) =>
        profile.Resolve(context, action).Select(chord => chord.ToString()).ToArray();

    /// <summary>chat:externalEditor 双键位：ctrl+g + ctrl+x ctrl+e（基准 defaultBindings.ts:83-84）。</summary>
    [Fact]
    public void Default_ChatExternalEditorHasBothChords()
    {
        var keys = KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor);
        Assert.Equal(2, keys.Length);
        Assert.Contains("ctrl+g", keys);
        Assert.Contains("ctrl+x ctrl+e", keys);
    }

    /// <summary>Global 保留键：ctrl+c→app:interrupt、ctrl+d→app:exit（基准 defaultBindings.ts:40-41）。</summary>
    [Fact]
    public void Default_GlobalInterruptAndExitKeys()
    {
        Assert.Contains("ctrl+c", KeysOf(KeybindingProfile.Default, KeybindingContext.Global, KeybindingAction.AppInterrupt));
        Assert.Contains("ctrl+d", KeysOf(KeybindingProfile.Default, KeybindingContext.Global, KeybindingAction.AppExit));
    }

    /// <summary>Chat escape→chat:cancel、enter→chat:submit（基准 defaultBindings.ts:66/73）。</summary>
    [Fact]
    public void Default_ChatEscapeAndEnter()
    {
        Assert.Contains("escape", KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatCancel));
        Assert.Equal("enter", KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatSubmit).Single());
    }

    /// <summary>ctrl+o→app:toggleTranscript（Global，基准 defaultBindings.ts:44 — 任务清单"transcript:toggle=ctrl+o"以基准为准）。</summary>
    [Fact]
    public void Default_ToggleTranscriptIsCtrlO()
    {
        Assert.Contains("ctrl+o", KeysOf(KeybindingProfile.Default, KeybindingContext.Global, KeybindingAction.AppToggleTranscript));
    }

    /// <summary>chat:undo 双键位 ctrl+_ / ctrl+shift+-（同码 \x1f，基准 defaultBindings.ts:80-81）。</summary>
    [Fact]
    public void Default_ChatUndoHasTwoBindings()
    {
        var keys = KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatUndo);
        Assert.Equal(2, keys.Length);
        Assert.Contains("ctrl+_", keys);
        Assert.Contains("ctrl+shift+-", keys);
    }

    /// <summary>transcript:exit 三键位 ctrl+c/escape/q（基准 defaultBindings.ts:164-168）。</summary>
    [Fact]
    public void Default_TranscriptExitHasThreeKeys()
    {
        var keys = KeysOf(KeybindingProfile.Default, KeybindingContext.Transcript, KeybindingAction.TranscriptExit);
        Assert.Equal(3, keys.Length);
        Assert.Contains("ctrl+c", keys);
        Assert.Contains("escape", keys);
        Assert.Contains("q", keys);
    }

    /// <summary>chat:cycleMode=shift+tab（基准 defaultBindings.ts:30/69 — w2 有 VT 解析固定 shift+tab）。</summary>
    [Fact]
    public void Default_ChatCycleModeIsShiftTab()
    {
        Assert.Equal("shift+tab", KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatCycleMode).Single());
    }

    /// <summary>chat:imagePaste 平台分支（基准 defaultBindings.ts:12-15 — Windows alt+v / 其他 ctrl+v）。</summary>
    [Fact]
    public void Default_ChatImagePasteIsPlatformSpecific()
    {
        var expected = OperatingSystem.IsWindows() ? "alt+v" : "ctrl+v";
        Assert.Equal(expected, KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.ChatImagePaste).Single());
    }

    /// <summary>chat:killAgents=ctrl+x ctrl+k 序列键（基准 defaultBindings.ts:68）。</summary>
    [Fact]
    public void Default_ChatKillAgentsIsSequence()
    {
        var chord = KeybindingProfile.Default.Resolve(KeybindingContext.Chat, KeybindingAction.ChatKillAgents).Single();
        Assert.True(chord.IsSequence);
        Assert.Equal("ctrl+x ctrl+k", chord.ToString());
    }

    /// <summary>HistorySearch 块抽查：ctrl+r→next、enter→execute（基准 defaultBindings.ts:174-178）。</summary>
    [Fact]
    public void Default_HistorySearchBlock()
    {
        Assert.Contains("ctrl+r", KeysOf(KeybindingProfile.Default, KeybindingContext.HistorySearch, KeybindingAction.HistorySearchNext));
        Assert.Contains("enter", KeysOf(KeybindingProfile.Default, KeybindingContext.HistorySearch, KeybindingAction.HistorySearchExecute));
    }

    /// <summary>Resolve 键级回退 Global：Chat 无 app:redraw → Global ctrl+l（对齐基准 Global 兜底）。</summary>
    [Fact]
    public void Resolve_FallsBackToGlobal()
    {
        Assert.Contains("ctrl+l", KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.AppRedraw));
        Assert.Contains("ctrl+r", KeysOf(KeybindingProfile.Default, KeybindingContext.Chat, KeybindingAction.HistorySearch));
    }

    /// <summary>Resolve 两级皆无 → 空列表（不是 null — 调用方安全）。</summary>
    [Fact]
    public void Resolve_UnknownEverywhereReturnsEmpty()
    {
        Assert.Empty(KeybindingProfile.Default.Resolve(KeybindingContext.Select, KeybindingAction.ChatStash));
    }

    /// <summary>MatchAction 单键反查：Chat ctrl+g→ChatExternalEditor、escape→ChatCancel。</summary>
    [Fact]
    public void MatchAction_ResolvesSingleKeyBindings()
    {
        Assert.Equal(KeybindingAction.ChatExternalEditor, KeybindingProfile.Default.MatchAction(KeybindingContext.Chat, Ctrl('g')));
        Assert.Equal(KeybindingAction.ChatCancel, KeybindingProfile.Default.MatchAction(KeybindingContext.Chat, KeyPress.Named(KeyKind.Escape)));
        Assert.Equal(KeybindingAction.ChatSubmit, KeybindingProfile.Default.MatchAction(KeybindingContext.Chat, KeyPress.Named(KeyKind.Return)));
    }

    /// <summary>MatchAction 键级回退 Global：Chat 无 ctrl+t → Global AppToggleTodos。</summary>
    [Fact]
    public void MatchAction_FallsBackToGlobal()
    {
        Assert.Equal(KeybindingAction.AppToggleTodos, KeybindingProfile.Default.MatchAction(KeybindingContext.Chat, Ctrl('t')));
    }

    /// <summary>MatchAction 精确 context 优先于 Global：Confirmation ctrl+d→PermissionToggleDebug（不落到 Global ctrl+d→AppExit）。</summary>
    [Fact]
    public void MatchAction_SpecificContextWinsOverGlobal()
    {
        Assert.Equal(
            KeybindingAction.PermissionToggleDebug,
            KeybindingProfile.Default.MatchAction(KeybindingContext.Confirmation, Ctrl('d')));
    }

    /// <summary>序列首键不触发单键匹配（ctrl+x 单击在 Chat 无单键绑定 — 对齐基准 resolver Phase 1 跳过多键）。</summary>
    [Fact]
    public void MatchAction_SequenceFirstStepDoesNotFire()
    {
        Assert.Null(KeybindingProfile.Default.MatchAction(KeybindingContext.Chat, Ctrl('x')));
    }

    /// <summary>IsSequenceStart 挂起判定：ctrl+x 是 Chat 序列首键（ctrl+x ctrl+e / ctrl+x ctrl+k）；ctrl+g 不是。</summary>
    [Fact]
    public void IsSequenceStart_DetectsPendingFirstSteps()
    {
        Assert.True(KeybindingProfile.Default.IsSequenceStart(KeybindingContext.Chat, Ctrl('x')));
        Assert.False(KeybindingProfile.Default.IsSequenceStart(KeybindingContext.Chat, Ctrl('g')));
        Assert.False(KeybindingProfile.Default.IsSequenceStart(KeybindingContext.Chat, KeyPress.Named(KeyKind.Escape)));
    }

    /// <summary>MatchSequence 完整触发：ctrl+x ctrl+e→ChatExternalEditor、ctrl+x ctrl+k→ChatKillAgents。</summary>
    [Fact]
    public void MatchSequence_FiresOnCompleteSequences()
    {
        Assert.Equal(
            KeybindingAction.ChatExternalEditor,
            KeybindingProfile.Default.MatchSequence(KeybindingContext.Chat, [Ctrl('x'), Ctrl('e')]));
        Assert.Equal(
            KeybindingAction.ChatKillAgents,
            KeybindingProfile.Default.MatchSequence(KeybindingContext.Chat, [Ctrl('x'), Ctrl('k')]));
        Assert.Null(KeybindingProfile.Default.MatchSequence(KeybindingContext.Chat, [Ctrl('x'), Ctrl('g')]));
    }

    /// <summary>Of 构造：同 (context, action) 多行聚合为多键位 + 自定义表回退。</summary>
    [Fact]
    public void Of_AggregatesMultipleChordsPerAction()
    {
        var profile = KeybindingProfile.Of(
        [
            (KeybindingContext.Global, KeybindingAction.AppRedraw, KeyChord.Parse("ctrl+l")),
            (KeybindingContext.Global, KeybindingAction.AppRedraw, KeyChord.Parse("f5")),
            (KeybindingContext.Chat, KeybindingAction.ChatSubmit, KeyChord.Parse("enter")),
        ]);
        Assert.Equal(2, profile.Resolve(KeybindingContext.Global, KeybindingAction.AppRedraw).Count);
        Assert.Contains("ctrl+l", KeysOf(profile, KeybindingContext.Global, KeybindingAction.AppRedraw));
        Assert.Contains("f5", KeysOf(profile, KeybindingContext.Global, KeybindingAction.AppRedraw));
        // Chat 无 AppRedraw → Global 回退仍可见
        Assert.Equal(2, profile.Resolve(KeybindingContext.Chat, KeybindingAction.AppRedraw).Count);
    }
}
