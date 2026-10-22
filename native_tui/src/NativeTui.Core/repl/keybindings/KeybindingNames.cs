namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位名称解析 — context/action 枚举 ↔ 用户 json 字符串名的唯一数据源
/// （对齐基准 keybindings/schema.ts KEYBINDING_CONTEXTS / KEYBINDING_ACTIONS 清单）。
/// context 名 = 枚举名小写（"chat"）；action 名 = 基准原名（"chat:externalEditor"，冒号前缀分组）。
/// 两者均大小写不敏感匹配（用户手打容错；两表归一小写后无碰撞）；
/// 错误提示经 SuggestActions 生成正确写法引导（AGENTS 规范：错误必须告诉 AI 正确写法）。
/// </summary>
public static class KeybindingNames
{
    /// <summary>context 枚举 ↔ json 名对照表（名即枚举名，20 项 — 基准 schema.ts:12-32 + Scroll/MessageActions 补编）。</summary>
    public static readonly (KeybindingContext Context, string Name)[] Contexts =
    [
        (KeybindingContext.Global, "Global"),
        (KeybindingContext.Chat, "Chat"),
        (KeybindingContext.Autocomplete, "Autocomplete"),
        (KeybindingContext.Settings, "Settings"),
        (KeybindingContext.Confirmation, "Confirmation"),
        (KeybindingContext.Tabs, "Tabs"),
        (KeybindingContext.Transcript, "Transcript"),
        (KeybindingContext.HistorySearch, "HistorySearch"),
        (KeybindingContext.Task, "Task"),
        (KeybindingContext.ThemePicker, "ThemePicker"),
        (KeybindingContext.Scroll, "Scroll"),
        (KeybindingContext.Help, "Help"),
        (KeybindingContext.Attachments, "Attachments"),
        (KeybindingContext.Footer, "Footer"),
        (KeybindingContext.MessageSelector, "MessageSelector"),
        (KeybindingContext.DiffDialog, "DiffDialog"),
        (KeybindingContext.ModelPicker, "ModelPicker"),
        (KeybindingContext.Select, "Select"),
        (KeybindingContext.Plugin, "Plugin"),
        (KeybindingContext.MessageActions, "MessageActions"),
    ];

    /// <summary>action 枚举 ↔ 基准原名对照表（105 项 — schema.ts:64-172 的 87 项 + 默认表补编 18 项）。</summary>
    public static readonly (KeybindingAction Action, string Name)[] Actions =
    [
        // App 级（schema.ts:64-75）
        (KeybindingAction.AppInterrupt, "app:interrupt"),
        (KeybindingAction.AppExit, "app:exit"),
        (KeybindingAction.AppToggleTodos, "app:toggleTodos"),
        (KeybindingAction.AppToggleTranscript, "app:toggleTranscript"),
        (KeybindingAction.AppToggleBrief, "app:toggleBrief"),
        (KeybindingAction.AppToggleTeammatePreview, "app:toggleTeammatePreview"),
        (KeybindingAction.AppToggleTerminal, "app:toggleTerminal"),
        (KeybindingAction.AppRedraw, "app:redraw"),
        (KeybindingAction.AppGlobalSearch, "app:globalSearch"),
        (KeybindingAction.AppQuickOpen, "app:quickOpen"),
        // 历史（schema.ts:76-79）
        (KeybindingAction.HistorySearch, "history:search"),
        (KeybindingAction.HistoryPrevious, "history:previous"),
        (KeybindingAction.HistoryNext, "history:next"),
        // 聊天（schema.ts:81-93）
        (KeybindingAction.ChatCancel, "chat:cancel"),
        (KeybindingAction.ChatKillAgents, "chat:killAgents"),
        (KeybindingAction.ChatCycleMode, "chat:cycleMode"),
        (KeybindingAction.ChatModelPicker, "chat:modelPicker"),
        (KeybindingAction.ChatFastMode, "chat:fastMode"),
        (KeybindingAction.ChatThinkingToggle, "chat:thinkingToggle"),
        (KeybindingAction.ChatSubmit, "chat:submit"),
        (KeybindingAction.ChatNewline, "chat:newline"),
        (KeybindingAction.ChatUndo, "chat:undo"),
        (KeybindingAction.ChatExternalEditor, "chat:externalEditor"),
        (KeybindingAction.ChatStash, "chat:stash"),
        (KeybindingAction.ChatImagePaste, "chat:imagePaste"),
        (KeybindingAction.ChatMessageActions, "chat:messageActions"),
        // 自动补全（schema.ts:94-98）
        (KeybindingAction.AutocompleteAccept, "autocomplete:accept"),
        (KeybindingAction.AutocompleteDismiss, "autocomplete:dismiss"),
        (KeybindingAction.AutocompletePrevious, "autocomplete:previous"),
        (KeybindingAction.AutocompleteNext, "autocomplete:next"),
        // 确认（schema.ts:99-108）
        (KeybindingAction.ConfirmYes, "confirm:yes"),
        (KeybindingAction.ConfirmNo, "confirm:no"),
        (KeybindingAction.ConfirmPrevious, "confirm:previous"),
        (KeybindingAction.ConfirmNext, "confirm:next"),
        (KeybindingAction.ConfirmNextField, "confirm:nextField"),
        (KeybindingAction.ConfirmPreviousField, "confirm:previousField"),
        (KeybindingAction.ConfirmCycleMode, "confirm:cycleMode"),
        (KeybindingAction.ConfirmToggle, "confirm:toggle"),
        (KeybindingAction.ConfirmToggleExplanation, "confirm:toggleExplanation"),
        // 标签（schema.ts:109-111）
        (KeybindingAction.TabsNext, "tabs:next"),
        (KeybindingAction.TabsPrevious, "tabs:previous"),
        // 转录（schema.ts:112-114）
        (KeybindingAction.TranscriptToggleShowAll, "transcript:toggleShowAll"),
        (KeybindingAction.TranscriptExit, "transcript:exit"),
        // 历史搜索（schema.ts:115-119）
        (KeybindingAction.HistorySearchNext, "historySearch:next"),
        (KeybindingAction.HistorySearchAccept, "historySearch:accept"),
        (KeybindingAction.HistorySearchCancel, "historySearch:cancel"),
        (KeybindingAction.HistorySearchExecute, "historySearch:execute"),
        // 任务（schema.ts:120-121）
        (KeybindingAction.TaskBackground, "task:background"),
        // 主题（schema.ts:122-123）
        (KeybindingAction.ThemeToggleSyntaxHighlighting, "theme:toggleSyntaxHighlighting"),
        // 帮助（schema.ts:124-125）
        (KeybindingAction.HelpDismiss, "help:dismiss"),
        // 附件（schema.ts:126-130）
        (KeybindingAction.AttachmentsNext, "attachments:next"),
        (KeybindingAction.AttachmentsPrevious, "attachments:previous"),
        (KeybindingAction.AttachmentsRemove, "attachments:remove"),
        (KeybindingAction.AttachmentsExit, "attachments:exit"),
        // 页脚（schema.ts:131-138）
        (KeybindingAction.FooterUp, "footer:up"),
        (KeybindingAction.FooterDown, "footer:down"),
        (KeybindingAction.FooterNext, "footer:next"),
        (KeybindingAction.FooterPrevious, "footer:previous"),
        (KeybindingAction.FooterOpenSelected, "footer:openSelected"),
        (KeybindingAction.FooterClearSelection, "footer:clearSelection"),
        (KeybindingAction.FooterClose, "footer:close"),
        // 消息选择器（schema.ts:139-144）
        (KeybindingAction.MessageSelectorUp, "messageSelector:up"),
        (KeybindingAction.MessageSelectorDown, "messageSelector:down"),
        (KeybindingAction.MessageSelectorTop, "messageSelector:top"),
        (KeybindingAction.MessageSelectorBottom, "messageSelector:bottom"),
        (KeybindingAction.MessageSelectorSelect, "messageSelector:select"),
        // Diff（schema.ts:145-151）
        (KeybindingAction.DiffDismiss, "diff:dismiss"),
        (KeybindingAction.DiffPreviousSource, "diff:previousSource"),
        (KeybindingAction.DiffNextSource, "diff:nextSource"),
        (KeybindingAction.DiffBack, "diff:back"),
        (KeybindingAction.DiffViewDetails, "diff:viewDetails"),
        (KeybindingAction.DiffPreviousFile, "diff:previousFile"),
        (KeybindingAction.DiffNextFile, "diff:nextFile"),
        // 模型选择器（schema.ts:152-155）
        (KeybindingAction.ModelPickerDecreaseEffort, "modelPicker:decreaseEffort"),
        (KeybindingAction.ModelPickerIncreaseEffort, "modelPicker:increaseEffort"),
        // 选择组件（schema.ts:156-160）
        (KeybindingAction.SelectNext, "select:next"),
        (KeybindingAction.SelectPrevious, "select:previous"),
        (KeybindingAction.SelectAccept, "select:accept"),
        (KeybindingAction.SelectCancel, "select:cancel"),
        // 插件（schema.ts:161-163）
        (KeybindingAction.PluginToggle, "plugin:toggle"),
        (KeybindingAction.PluginInstall, "plugin:install"),
        // 权限（schema.ts:164-165）
        (KeybindingAction.PermissionToggleDebug, "permission:toggleDebug"),
        // 设置（schema.ts:166-169）
        (KeybindingAction.SettingsSearch, "settings:search"),
        (KeybindingAction.SettingsRetry, "settings:retry"),
        (KeybindingAction.SettingsClose, "settings:close"),
        // 语音（schema.ts:170-171）
        (KeybindingAction.VoicePushToTalk, "voice:pushToTalk"),
        // 滚动/选区（defaultBindings.ts:195-213 补编）
        (KeybindingAction.ScrollPageUp, "scroll:pageUp"),
        (KeybindingAction.ScrollPageDown, "scroll:pageDown"),
        (KeybindingAction.ScrollLineUp, "scroll:lineUp"),
        (KeybindingAction.ScrollLineDown, "scroll:lineDown"),
        (KeybindingAction.ScrollTop, "scroll:top"),
        (KeybindingAction.ScrollBottom, "scroll:bottom"),
        (KeybindingAction.SelectionCopy, "selection:copy"),
        // 消息动作条（defaultBindings.ts:267-295 补编）
        (KeybindingAction.MessageActionsPrev, "messageActions:prev"),
        (KeybindingAction.MessageActionsNext, "messageActions:next"),
        (KeybindingAction.MessageActionsTop, "messageActions:top"),
        (KeybindingAction.MessageActionsBottom, "messageActions:bottom"),
        (KeybindingAction.MessageActionsPrevUser, "messageActions:prevUser"),
        (KeybindingAction.MessageActionsNextUser, "messageActions:nextUser"),
        (KeybindingAction.MessageActionsEscape, "messageActions:escape"),
        (KeybindingAction.MessageActionsCtrlC, "messageActions:ctrlc"),
        (KeybindingAction.MessageActionsEnter, "messageActions:enter"),
        (KeybindingAction.MessageActionsC, "messageActions:c"),
        (KeybindingAction.MessageActionsP, "messageActions:p"),
    ];

    /// <summary>context 名 → 枚举（大小写不敏感）。</summary>
    private static readonly FrozenDictionary<string, KeybindingContext> s_contextsByName =
        Contexts.ToFrozenDictionary(row => row.Name, row => row.Context, StringComparer.OrdinalIgnoreCase);

    /// <summary>action 原名 → 枚举（大小写不敏感）。</summary>
    private static readonly FrozenDictionary<string, KeybindingAction> s_actionsByName =
        Actions.ToFrozenDictionary(row => row.Name, row => row.Action, StringComparer.OrdinalIgnoreCase);

    /// <summary>枚举 → action 原名（错误提示/日志用）。</summary>
    private static readonly FrozenDictionary<KeybindingAction, string> s_nameByAction =
        Actions.ToFrozenDictionary(row => row.Action, row => row.Name);

    /// <summary>解析用户 json 的 context 名（大小写不敏感；"chat"/"CHAT"/"Chat" 均命中）。</summary>
    /// <param name="name">用户 json 中的 context 键名。</param>
    /// <param name="context">解析出的上下文枚举。</param>
    /// <returns>是否在 20 个有效上下文清单内。</returns>
    public static bool TryResolveContext(string name, out KeybindingContext context) =>
        s_contextsByName.TryGetValue(name, out context);

    /// <summary>解析用户 json 的 action 名（大小写不敏感；"chat:externalEditor"/"CHAT:EXTERNALEDITOR" 均命中）。</summary>
    /// <param name="name">用户 json 中的 action 键名。</param>
    /// <param name="action">解析出的动作枚举。</param>
    /// <returns>是否在 105 个有效动作清单内。</returns>
    public static bool TryResolveAction(string name, out KeybindingAction action) =>
        s_actionsByName.TryGetValue(name, out action);

    /// <summary>action 枚举 → 基准原名（"chat:externalEditor"）。</summary>
    public static string ToName(this KeybindingAction action) => s_nameByAction[action];

    /// <summary>context 枚举 → 用户 json 名（小写，"chat"）。</summary>
    public static string ToName(this KeybindingContext context) => context.ToString().ToLowerInvariant();

    /// <summary>
    /// 未知 action 的正确写法提示（错误引导四要素之"接下来怎么做"）：
    /// 名字含冒号前缀且前缀命中 → 列出该前缀全部 action 原名；
    /// 前缀无效 → 列出全部有效前缀（app/chat/history/...），引导用户先写对前缀。
    /// </summary>
    /// <param name="actionName">用户输错的 action 名。</param>
    /// <returns>可直接拼进错误消息的提示文本。</returns>
    public static string SuggestActions(string actionName)
    {
        var prefix = actionName.Split(':', 2)[0];
        var matches = Actions
            .Where(row => row.Name.StartsWith(prefix + ":", StringComparison.Ordinal))
            .Select(row => row.Name)
            .ToArray();
        return matches.Length > 0
            ? $"该前缀可用 action：{string.Join("、", matches)}"
            : $"有效前缀：{string.Join("、", Actions.Select(row => row.Name.Split(':', 2)[0]).Distinct())}";
    }

    /// <summary>有效 context 清单提示文本（未知 context 错误的正确写法引导）。</summary>
    public static string ContextsHint => $"有效 context：{string.Join("、", Contexts.Select(row => row.Name))}";
}
