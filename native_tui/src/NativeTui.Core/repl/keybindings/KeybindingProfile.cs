namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位绑定表 — (context, action) → 键序列列表（一 action 可多键）。
/// 对齐基准 keybindings/resolver.ts 查询语义：context 未命中回退 Global（键级回退 —
/// 精确 context 桶里没有该键绑定才查 Global 桶，Global 是兜底）；
/// 单键匹配只看单键绑定（序列键不拦截单键事件，对齐基准 resolveKey Phase 1 跳过 chord.length !== 1）。
/// <see cref="Default"/> 从基准 defaultBindings.ts 全量转录（非 feature 条件绑定 —
/// KAIROS/QUICK_SEARCH/TERMINAL_PANEL/MESSAGE_ACTIONS/VOICE_MODE 条件键不启用，action 枚举仍保留全量）。
/// </summary>
public sealed class KeybindingProfile
{
    /// <summary>(context, action) → 键序列数组（同键多行聚合，保持声明序）。</summary>
    private readonly FrozenDictionary<(KeybindingContext, KeybindingAction), KeyChord[]> _byAction;

    /// <summary>context → (键序列, action) 条目数组（按键反查索引 — MatchAction/序列查询用）。</summary>
    private readonly FrozenDictionary<KeybindingContext, (KeyChord Chord, KeybindingAction Action)[]> _byContext;

    /// <summary>
    /// 默认绑定表 — 基准 defaultBindings.ts 全量转录（116 条，见 <see cref="DefaultRows"/> 各行基准行号注释）。
    /// 首次访问触发常量键位串解析（转录笔误会抛 FormatException 立即暴露 — 开心路径断言语义）。
    /// </summary>
    public static KeybindingProfile Default { get; } = Of(
        DefaultRows().Select(row => (row.Context, row.Action, KeyChord.Parse(row.Keys))));

    /// <summary>私有构造 — 经 <see cref="Of"/> 聚合构建。</summary>
    private KeybindingProfile(
        FrozenDictionary<(KeybindingContext, KeybindingAction), KeyChord[]> byAction,
        FrozenDictionary<KeybindingContext, (KeyChord Chord, KeybindingAction Action)[]> byContext)
    {
        _byAction = byAction;
        _byContext = byContext;
    }

    /// <summary>
    /// 从绑定行聚合构建 profile：同 (context, action) 多行 → 多键位（声明序保持）；
    /// 同时建立按键反查索引。
    /// </summary>
    /// <param name="rows">绑定行集合。</param>
    /// <returns>聚合后的 profile。</returns>
    public static KeybindingProfile Of(IEnumerable<(KeybindingContext Context, KeybindingAction Action, KeyChord Chord)> rows)
    {
        var byAction = new Dictionary<(KeybindingContext, KeybindingAction), List<KeyChord>>();
        var byContext = new Dictionary<KeybindingContext, List<(KeyChord Chord, KeybindingAction Action)>>();
        foreach (var row in rows)
        {
            AddEntry(byAction, (row.Context, row.Action), row.Chord);
            AddEntry(byContext, row.Context, (row.Chord, row.Action));
        }

        return new KeybindingProfile(
            byAction.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
            byContext.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
    }

    /// <summary>展开为绑定行（internal — KeybindingLoader 合并默认表用；顺序不保证，消费方不依赖声明序）。</summary>
    internal IEnumerable<(KeybindingContext Context, KeybindingAction Action, KeyChord Chord)> Rows =>
        _byAction.SelectMany(pair => pair.Value.Select(chord => (pair.Key.Item1, pair.Key.Item2, chord)));

    /// <summary>
    /// 查询 action 的键序列列表（context 未命中回退 Global — 对齐基准 resolver.ts Global 兜底语义）。
    /// </summary>
    /// <param name="context">查询上下文。</param>
    /// <param name="action">目标动作。</param>
    /// <returns>键序列列表（无绑定返回空列表；序列键与单键混排）。</returns>
    public IReadOnlyList<KeyChord> Resolve(KeybindingContext context, KeybindingAction action) =>
        _byAction.TryGetValue((context, action), out var chords)
            ? chords
            : _byAction.TryGetValue((KeybindingContext.Global, action), out var globalChords)
                ? globalChords
                : Array.Empty<KeyChord>();

    /// <summary>
    /// 单键反查 — 当前按键在 context 下触发的 action（键级回退 Global：精确 context 桶无该键绑定才查 Global 桶）。
    /// 序列键不参与（对齐基准 resolveKey 只匹配 chord.length === 1 — ctrl+x 单击不会误触发 ctrl+x ctrl+e）。
    /// </summary>
    /// <param name="context">查询上下文。</param>
    /// <param name="key">当前按键。</param>
    /// <returns>命中的 action；无命中返回 null。</returns>
    public KeybindingAction? MatchAction(KeybindingContext context, KeyPress key)
    {
        foreach (var entry in Entries(context))
        {
            if (entry.Chord.Matches(key))
            {
                return entry.Action;
            }
        }

        return null;
    }

    /// <summary>序列挂起判定 — 该按键是否为 context（含 Global 回退）下任一序列键的首键（置前缀态等待后续）。</summary>
    /// <param name="context">查询上下文。</param>
    /// <param name="key">当前按键。</param>
    /// <returns>存在以该键开头的序列键。</returns>
    public bool IsSequenceStart(KeybindingContext context, KeyPress key) =>
        Entries(context).Any(entry => entry.Chord.IsPrefix(key));

    /// <summary>序列完整触发判定 — 已按序列在 context（含 Global 回退）下命中的 action。</summary>
    /// <param name="context">查询上下文。</param>
    /// <param name="pressed">已按下的完整按键序列。</param>
    /// <returns>命中的 action；无命中返回 null。</returns>
    public KeybindingAction? MatchSequence(KeybindingContext context, ReadOnlySpan<KeyPress> pressed)
    {
        foreach (var entry in Entries(context))
        {
            if (entry.Chord.MatchesSequence(pressed))
            {
                return entry.Action;
            }
        }

        return null;
    }

    /// <summary>遍历 context 桶 + Global 兜底桶的条目（键级回退的统一入口 — 查询 API 共用）。</summary>
    /// <param name="context">查询上下文。</param>
    /// <returns>精确桶在前、Global 兜底桶在后的条目流。</returns>
    private IEnumerable<(KeyChord Chord, KeybindingAction Action)> Entries(KeybindingContext context)
    {
        if (_byContext.TryGetValue(context, out var entries))
        {
            foreach (var entry in entries)
            {
                yield return entry;
            }
        }

        if (context != KeybindingContext.Global && _byContext.TryGetValue(KeybindingContext.Global, out var globalEntries))
        {
            foreach (var entry in globalEntries)
            {
                yield return entry;
            }
        }
    }

    /// <summary>聚合辅助 — 字典条目不存在则建列表后追加（避免 TryGetValue/双索引重复样板）。</summary>
    private static void AddEntry<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out var list))
        {
            list.Add(value);
        }
        else
        {
            map[key] = [value];
        }
    }

    /// <summary>
    /// 默认绑定行 — 基准 defaultBindings.ts 全量转录（字符串键位经 KeyChord.Parse）。
    /// 条件键不启用：KAIROS(ctrl+shift+b)/QUICK_SEARCH(ctrl+shift+f 等 4 键)/TERMINAL_PANEL(meta+j)/
    /// MESSAGE_ACTIONS(Chat shift+up + MessageActions 块)/VOICE_MODE(space) — feature gate 基准默认关闭。
    /// 平台分支键（对齐基准 defaultBindings.ts:12-30）：imagePaste — Windows alt+v（ctrl+v 被系统粘贴占用）、
    /// 其他平台 ctrl+v；cycleMode — 固定 shift+tab（w2 KeyPressParser 支持 VT ESC[Z 解析，无基准 Windows 无 VT 回退需求）。
    /// w2 键模型无法表达的转录跳过：wheelup/wheeldown（无 wheel KeyKind）、cmd+c（无 Super 修饰）。
    /// </summary>
    /// <returns>(上下文, 动作, 键位串) 默认绑定行。</returns>
    private static IEnumerable<(KeybindingContext Context, KeybindingAction Action, string Keys)> DefaultRows() =>
    [
        // Global（基准 defaultBindings.ts:33-61 — ctrl+c/ctrl+d 为保留键，见 ReservedShortcuts）
        (KeybindingContext.Global, KeybindingAction.AppInterrupt, "ctrl+c"),
        (KeybindingContext.Global, KeybindingAction.AppExit, "ctrl+d"),
        (KeybindingContext.Global, KeybindingAction.AppRedraw, "ctrl+l"),
        (KeybindingContext.Global, KeybindingAction.AppToggleTodos, "ctrl+t"),
        (KeybindingContext.Global, KeybindingAction.AppToggleTranscript, "ctrl+o"),
        (KeybindingContext.Global, KeybindingAction.AppToggleTeammatePreview, "ctrl+shift+o"),
        (KeybindingContext.Global, KeybindingAction.HistorySearch, "ctrl+r"),

        // Chat（基准 defaultBindings.ts:63-97）
        (KeybindingContext.Chat, KeybindingAction.ChatCancel, "escape"),
        (KeybindingContext.Chat, KeybindingAction.ChatKillAgents, "ctrl+x ctrl+k"),
        (KeybindingContext.Chat, KeybindingAction.ChatCycleMode, "shift+tab"),
        (KeybindingContext.Chat, KeybindingAction.ChatModelPicker, "meta+p"),
        (KeybindingContext.Chat, KeybindingAction.ChatFastMode, "meta+o"),
        (KeybindingContext.Chat, KeybindingAction.ChatThinkingToggle, "meta+t"),
        (KeybindingContext.Chat, KeybindingAction.ChatSubmit, "enter"),
        (KeybindingContext.Chat, KeybindingAction.HistoryPrevious, "up"),
        (KeybindingContext.Chat, KeybindingAction.HistoryNext, "down"),
        (KeybindingContext.Chat, KeybindingAction.ChatUndo, "ctrl+_"),
        (KeybindingContext.Chat, KeybindingAction.ChatUndo, "ctrl+shift+-"),
        (KeybindingContext.Chat, KeybindingAction.ChatExternalEditor, "ctrl+x ctrl+e"),
        (KeybindingContext.Chat, KeybindingAction.ChatExternalEditor, "ctrl+g"),
        (KeybindingContext.Chat, KeybindingAction.ChatStash, "ctrl+s"),
        (KeybindingContext.Chat, KeybindingAction.ChatImagePaste, OperatingSystem.IsWindows() ? "alt+v" : "ctrl+v"),

        // Autocomplete（基准 defaultBindings.ts:99-107）
        (KeybindingContext.Autocomplete, KeybindingAction.AutocompleteAccept, "tab"),
        (KeybindingContext.Autocomplete, KeybindingAction.AutocompleteDismiss, "escape"),
        (KeybindingContext.Autocomplete, KeybindingAction.AutocompletePrevious, "up"),
        (KeybindingContext.Autocomplete, KeybindingAction.AutocompleteNext, "down"),

        // Settings（基准 defaultBindings.ts:108-129）
        (KeybindingContext.Settings, KeybindingAction.ConfirmNo, "escape"),
        (KeybindingContext.Settings, KeybindingAction.SelectPrevious, "up"),
        (KeybindingContext.Settings, KeybindingAction.SelectNext, "down"),
        (KeybindingContext.Settings, KeybindingAction.SelectPrevious, "k"),
        (KeybindingContext.Settings, KeybindingAction.SelectNext, "j"),
        (KeybindingContext.Settings, KeybindingAction.SelectPrevious, "ctrl+p"),
        (KeybindingContext.Settings, KeybindingAction.SelectNext, "ctrl+n"),
        (KeybindingContext.Settings, KeybindingAction.SelectAccept, "space"),
        (KeybindingContext.Settings, KeybindingAction.SettingsClose, "enter"),
        (KeybindingContext.Settings, KeybindingAction.SettingsSearch, "/"),
        (KeybindingContext.Settings, KeybindingAction.SettingsRetry, "r"),

        // Confirmation（基准 defaultBindings.ts:130-149）
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmYes, "y"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmNo, "n"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmYes, "enter"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmNo, "escape"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmPrevious, "up"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmNext, "down"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmNextField, "tab"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmToggle, "space"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmCycleMode, "shift+tab"),
        (KeybindingContext.Confirmation, KeybindingAction.ConfirmToggleExplanation, "ctrl+e"),
        (KeybindingContext.Confirmation, KeybindingAction.PermissionToggleDebug, "ctrl+d"),

        // Tabs（基准 defaultBindings.ts:150-159）
        (KeybindingContext.Tabs, KeybindingAction.TabsNext, "tab"),
        (KeybindingContext.Tabs, KeybindingAction.TabsPrevious, "shift+tab"),
        (KeybindingContext.Tabs, KeybindingAction.TabsNext, "right"),
        (KeybindingContext.Tabs, KeybindingAction.TabsPrevious, "left"),

        // Transcript（基准 defaultBindings.ts:160-170 — q 为 pager 惯例退出键）
        (KeybindingContext.Transcript, KeybindingAction.TranscriptToggleShowAll, "ctrl+e"),
        (KeybindingContext.Transcript, KeybindingAction.TranscriptExit, "ctrl+c"),
        (KeybindingContext.Transcript, KeybindingAction.TranscriptExit, "escape"),
        (KeybindingContext.Transcript, KeybindingAction.TranscriptExit, "q"),

        // HistorySearch（基准 defaultBindings.ts:171-180）
        (KeybindingContext.HistorySearch, KeybindingAction.HistorySearchNext, "ctrl+r"),
        (KeybindingContext.HistorySearch, KeybindingAction.HistorySearchAccept, "escape"),
        (KeybindingContext.HistorySearch, KeybindingAction.HistorySearchAccept, "tab"),
        (KeybindingContext.HistorySearch, KeybindingAction.HistorySearchCancel, "ctrl+c"),
        (KeybindingContext.HistorySearch, KeybindingAction.HistorySearchExecute, "enter"),

        // Task（基准 defaultBindings.ts:181-188）
        (KeybindingContext.Task, KeybindingAction.TaskBackground, "ctrl+b"),

        // ThemePicker（基准 defaultBindings.ts:189-194）
        (KeybindingContext.ThemePicker, KeybindingAction.ThemeToggleSyntaxHighlighting, "ctrl+t"),

        // Scroll（基准 defaultBindings.ts:195-213 — wheelup/wheeldown/cmd+c 因 w2 键模型无对应通道跳过）
        (KeybindingContext.Scroll, KeybindingAction.ScrollPageUp, "pageup"),
        (KeybindingContext.Scroll, KeybindingAction.ScrollPageDown, "pagedown"),
        (KeybindingContext.Scroll, KeybindingAction.ScrollTop, "ctrl+home"),
        (KeybindingContext.Scroll, KeybindingAction.ScrollBottom, "ctrl+end"),
        (KeybindingContext.Scroll, KeybindingAction.SelectionCopy, "ctrl+shift+c"),

        // Help（基准 defaultBindings.ts:214-219）
        (KeybindingContext.Help, KeybindingAction.HelpDismiss, "escape"),

        // Attachments（基准 defaultBindings.ts:220-231）
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsNext, "right"),
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsPrevious, "left"),
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsRemove, "backspace"),
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsRemove, "delete"),
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsExit, "down"),
        (KeybindingContext.Attachments, KeybindingAction.AttachmentsExit, "escape"),

        // Footer（基准 defaultBindings.ts:232-245）
        (KeybindingContext.Footer, KeybindingAction.FooterUp, "up"),
        (KeybindingContext.Footer, KeybindingAction.FooterUp, "ctrl+p"),
        (KeybindingContext.Footer, KeybindingAction.FooterDown, "down"),
        (KeybindingContext.Footer, KeybindingAction.FooterDown, "ctrl+n"),
        (KeybindingContext.Footer, KeybindingAction.FooterNext, "right"),
        (KeybindingContext.Footer, KeybindingAction.FooterPrevious, "left"),
        (KeybindingContext.Footer, KeybindingAction.FooterOpenSelected, "enter"),
        (KeybindingContext.Footer, KeybindingAction.FooterClearSelection, "escape"),

        // MessageSelector（基准 defaultBindings.ts:246-266）
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorUp, "up"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorDown, "down"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorUp, "k"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorDown, "j"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorUp, "ctrl+p"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorDown, "ctrl+n"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorTop, "ctrl+up"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorTop, "shift+up"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorTop, "meta+up"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorTop, "shift+k"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorBottom, "ctrl+down"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorBottom, "shift+down"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorBottom, "meta+down"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorBottom, "shift+j"),
        (KeybindingContext.MessageSelector, KeybindingAction.MessageSelectorSelect, "enter"),

        // MessageActions（基准 defaultBindings.ts:267-295 — MESSAGE_ACTIONS feature 条件块，不启用）

        // DiffDialog（基准 defaultBindings.ts:296-308）
        (KeybindingContext.DiffDialog, KeybindingAction.DiffDismiss, "escape"),
        (KeybindingContext.DiffDialog, KeybindingAction.DiffPreviousSource, "left"),
        (KeybindingContext.DiffDialog, KeybindingAction.DiffNextSource, "right"),
        (KeybindingContext.DiffDialog, KeybindingAction.DiffPreviousFile, "up"),
        (KeybindingContext.DiffDialog, KeybindingAction.DiffNextFile, "down"),
        (KeybindingContext.DiffDialog, KeybindingAction.DiffViewDetails, "enter"),

        // ModelPicker（基准 defaultBindings.ts:309-316）
        (KeybindingContext.ModelPicker, KeybindingAction.ModelPickerDecreaseEffort, "left"),
        (KeybindingContext.ModelPicker, KeybindingAction.ModelPickerIncreaseEffort, "right"),

        // Select（基准 defaultBindings.ts:317-330）
        (KeybindingContext.Select, KeybindingAction.SelectPrevious, "up"),
        (KeybindingContext.Select, KeybindingAction.SelectNext, "down"),
        (KeybindingContext.Select, KeybindingAction.SelectNext, "j"),
        (KeybindingContext.Select, KeybindingAction.SelectPrevious, "k"),
        (KeybindingContext.Select, KeybindingAction.SelectNext, "ctrl+n"),
        (KeybindingContext.Select, KeybindingAction.SelectPrevious, "ctrl+p"),
        (KeybindingContext.Select, KeybindingAction.SelectAccept, "enter"),
        (KeybindingContext.Select, KeybindingAction.SelectCancel, "escape"),

        // Plugin（基准 defaultBindings.ts:331-339）
        (KeybindingContext.Plugin, KeybindingAction.PluginToggle, "space"),
        (KeybindingContext.Plugin, KeybindingAction.PluginInstall, "i"),
    ];
}
