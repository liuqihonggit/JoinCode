namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位动作 — 全量对齐基准 keybindings/schema.ts KEYBINDING_ACTIONS（87 项）
/// + defaultBindings.ts 额外使用的 scroll:*/selection:*/messageActions:* 组（基准默认表有、schema 清单未收编，共 105 项）。
/// 命名规则：原名 "prefix:camelName" → PascalCase（chat:externalEditor → ChatExternalEditor）；
/// 原名与枚举的对照经 KeybindingNames.Actions 唯一数据源转换，禁止消费方硬编码字符串。
/// 标注 [默认表未绑定] 的值存在于合法清单但基准默认表无键位（用户 json 可自行绑定）。
/// </summary>
public enum KeybindingAction
{
    // ── App 级动作（Global 上下文，基准 schema.ts:64-75）──

    /// <summary>app:interrupt — 中断当前任务（Global ctrl+c，保留键双击语义）。</summary>
    AppInterrupt,

    /// <summary>app:exit — 退出应用（Global ctrl+d，保留键）。</summary>
    AppExit,

    /// <summary>app:toggleTodos — 切换 TODO 面板（Global ctrl+t）。</summary>
    AppToggleTodos,

    /// <summary>app:toggleTranscript — 切换转录视图（Global ctrl+o）。</summary>
    AppToggleTranscript,

    /// <summary>app:toggleBrief — 切换简报（KAIROS 特性条件键，默认表未绑定）。</summary>
    AppToggleBrief,

    /// <summary>app:toggleTeammatePreview — 切换队友预览（Global ctrl+shift+o）。</summary>
    AppToggleTeammatePreview,

    /// <summary>app:toggleTerminal — 切换终端面板（TERMINAL_PANEL 特性 meta+j，默认表未绑定）。</summary>
    AppToggleTerminal,

    /// <summary>app:redraw — 强制重绘（Global ctrl+l）。</summary>
    AppRedraw,

    /// <summary>app:globalSearch — 全局搜索（QUICK_SEARCH 特性 ctrl+shift+f，默认表未绑定）。</summary>
    AppGlobalSearch,

    /// <summary>app:quickOpen — 快速打开（QUICK_SEARCH 特性 ctrl+shift+p，默认表未绑定）。</summary>
    AppQuickOpen,

    // ── 历史导航（基准 schema.ts:76-79）──

    /// <summary>history:search — 进入历史搜索（Global ctrl+r）。</summary>
    HistorySearch,

    /// <summary>history:previous — 上一条历史（Chat up）。</summary>
    HistoryPrevious,

    /// <summary>history:next — 下一条历史（Chat down）。</summary>
    HistoryNext,

    // ── 聊天输入动作（基准 schema.ts:81-93）──

    /// <summary>chat:cancel — 取消当前输入/操作（Chat escape）。</summary>
    ChatCancel,

    /// <summary>chat:killAgents — 终止后台代理（Chat ctrl+x ctrl+k 序列键）。</summary>
    ChatKillAgents,

    /// <summary>chat:cycleMode — 循环权限模式（Chat shift+tab）。</summary>
    ChatCycleMode,

    /// <summary>chat:modelPicker — 打开模型选择器（Chat meta+p）。</summary>
    ChatModelPicker,

    /// <summary>chat:fastMode — 切换快速模式（Chat meta+o）。</summary>
    ChatFastMode,

    /// <summary>chat:thinkingToggle — 切换思考模式（Chat meta+t）。</summary>
    ChatThinkingToggle,

    /// <summary>chat:submit — 提交输入（Chat enter）。</summary>
    ChatSubmit,

    /// <summary>chat:newline — 插入换行（默认表未绑定，基准保留 action）。</summary>
    ChatNewline,

    /// <summary>chat:undo — 撤销输入编辑（Chat ctrl+_ 与 ctrl+shift+- 双键位，同码 \x1f）。</summary>
    ChatUndo,

    /// <summary>chat:externalEditor — 外部编辑器编辑输入（Chat ctrl+g 与 ctrl+x ctrl+e 序列双键位）。</summary>
    ChatExternalEditor,

    /// <summary>chat:stash — 收起当前输入（Chat ctrl+s）。</summary>
    ChatStash,

    /// <summary>chat:imagePaste — 粘贴图片（Chat alt+v[Windows]/ctrl+v[其他]）。</summary>
    ChatImagePaste,

    /// <summary>chat:messageActions — 打开消息动作条（MESSAGE_ACTIONS 特性 shift+up，默认表未绑定）。</summary>
    ChatMessageActions,

    // ── 自动补全动作（基准 schema.ts:94-98）──

    /// <summary>autocomplete:accept — 接受补全项（Autocomplete tab）。</summary>
    AutocompleteAccept,

    /// <summary>autocomplete:dismiss — 关闭补全菜单（Autocomplete escape）。</summary>
    AutocompleteDismiss,

    /// <summary>autocomplete:previous — 上一个补全项（Autocomplete up）。</summary>
    AutocompletePrevious,

    /// <summary>autocomplete:next — 下一个补全项（Autocomplete down）。</summary>
    AutocompleteNext,

    // ── 确认对话框动作（基准 schema.ts:99-108）──

    /// <summary>confirm:yes — 确认（Confirmation y / enter）。</summary>
    ConfirmYes,

    /// <summary>confirm:no — 取消（Confirmation n / escape / Settings escape）。</summary>
    ConfirmNo,

    /// <summary>confirm:previous — 上一个列表项（Confirmation up）。</summary>
    ConfirmPrevious,

    /// <summary>confirm:next — 下一个列表项（Confirmation down）。</summary>
    ConfirmNext,

    /// <summary>confirm:nextField — 跳到下一字段（Confirmation tab）。</summary>
    ConfirmNextField,

    /// <summary>confirm:previousField — 跳到上一字段（默认表未绑定，基准保留 action）。</summary>
    ConfirmPreviousField,

    /// <summary>confirm:cycleMode — 循环模式（文件权限/团队对话框，Confirmation shift+tab）。</summary>
    ConfirmCycleMode,

    /// <summary>confirm:toggle — 切换选中项（Confirmation space）。</summary>
    ConfirmToggle,

    /// <summary>confirm:toggleExplanation — 切换权限说明（Confirmation ctrl+e）。</summary>
    ConfirmToggleExplanation,

    // ── 标签导航动作（基准 schema.ts:109-111）──

    /// <summary>tabs:next — 下一个标签（Tabs tab / right）。</summary>
    TabsNext,

    /// <summary>tabs:previous — 上一个标签（Tabs shift+tab / left）。</summary>
    TabsPrevious,

    // ── 转录视图动作（基准 schema.ts:112-114）──

    /// <summary>transcript:toggleShowAll — 切换全部消息展开（Transcript ctrl+e）。</summary>
    TranscriptToggleShowAll,

    /// <summary>transcript:exit — 退出转录视图（Transcript ctrl+c / escape / q 三键位）。</summary>
    TranscriptExit,

    // ── 历史搜索动作（基准 schema.ts:115-119）──

    /// <summary>historySearch:next — 下一个匹配（HistorySearch ctrl+r）。</summary>
    HistorySearchNext,

    /// <summary>historySearch:accept — 接受当前匹配（HistorySearch escape / tab）。</summary>
    HistorySearchAccept,

    /// <summary>historySearch:cancel — 取消搜索（HistorySearch ctrl+c）。</summary>
    HistorySearchCancel,

    /// <summary>historySearch:execute — 接受并执行（HistorySearch enter）。</summary>
    HistorySearchExecute,

    // ── 任务动作（基准 schema.ts:120-121）──

    /// <summary>task:background — 前台任务转后台（Task ctrl+b）。</summary>
    TaskBackground,

    // ── 主题选择器动作（基准 schema.ts:122-123）──

    /// <summary>theme:toggleSyntaxHighlighting — 切换语法高亮（ThemePicker ctrl+t）。</summary>
    ThemeToggleSyntaxHighlighting,

    // ── 帮助动作（基准 schema.ts:124-125）──

    /// <summary>help:dismiss — 关闭帮助浮层（Help escape）。</summary>
    HelpDismiss,

    // ── 附件导航动作（基准 schema.ts:126-130）──

    /// <summary>attachments:next — 下一个附件（Attachments right）。</summary>
    AttachmentsNext,

    /// <summary>attachments:previous — 上一个附件（Attachments left）。</summary>
    AttachmentsPrevious,

    /// <summary>attachments:remove — 移除附件（Attachments backspace / delete）。</summary>
    AttachmentsRemove,

    /// <summary>attachments:exit — 退出附件导航（Attachments down / escape）。</summary>
    AttachmentsExit,

    // ── 页脚指示器动作（基准 schema.ts:131-138）──

    /// <summary>footer:up — 页脚上移（Footer up / ctrl+p）。</summary>
    FooterUp,

    /// <summary>footer:down — 页脚下移（Footer down / ctrl+n）。</summary>
    FooterDown,

    /// <summary>footer:next — 下一个指示器（Footer right）。</summary>
    FooterNext,

    /// <summary>footer:previous — 上一个指示器（Footer left）。</summary>
    FooterPrevious,

    /// <summary>footer:openSelected — 打开选中项（Footer enter）。</summary>
    FooterOpenSelected,

    /// <summary>footer:clearSelection — 清除选择（Footer escape）。</summary>
    FooterClearSelection,

    /// <summary>footer:close — 关闭页脚（默认表未绑定，基准保留 action）。</summary>
    FooterClose,

    // ── 消息选择器（rewind）动作（基准 schema.ts:139-144）──

    /// <summary>messageSelector:up — 上一条消息（MessageSelector up / k / ctrl+p）。</summary>
    MessageSelectorUp,

    /// <summary>messageSelector:down — 下一条消息（MessageSelector down / j / ctrl+n）。</summary>
    MessageSelectorDown,

    /// <summary>messageSelector:top — 跳到顶部（MessageSelector ctrl+up / shift+up / meta+up / shift+k）。</summary>
    MessageSelectorTop,

    /// <summary>messageSelector:bottom — 跳到底部（MessageSelector ctrl+down / shift+down / meta+down / shift+j）。</summary>
    MessageSelectorBottom,

    /// <summary>messageSelector:select — 选中消息（MessageSelector enter）。</summary>
    MessageSelectorSelect,

    // ── Diff 对话框动作（基准 schema.ts:145-151）──

    /// <summary>diff:dismiss — 关闭 diff 对话框（DiffDialog escape）。</summary>
    DiffDismiss,

    /// <summary>diff:previousSource — 上一个源（DiffDialog left）。</summary>
    DiffPreviousSource,

    /// <summary>diff:nextSource — 下一个源（DiffDialog right）。</summary>
    DiffNextSource,

    /// <summary>diff:back — 返回（默认表未绑定 — 基准注释：detail 模式由 left 承担）。</summary>
    DiffBack,

    /// <summary>diff:viewDetails — 查看详情（DiffDialog enter）。</summary>
    DiffViewDetails,

    /// <summary>diff:previousFile — 上一个文件（DiffDialog up）。</summary>
    DiffPreviousFile,

    /// <summary>diff:nextFile — 下一个文件（DiffDialog down）。</summary>
    DiffNextFile,

    // ── 模型选择器动作（基准 schema.ts:152-155）──

    /// <summary>modelPicker:decreaseEffort — 降低努力档位（ModelPicker left）。</summary>
    ModelPickerDecreaseEffort,

    /// <summary>modelPicker:increaseEffort — 提高努力档位（ModelPicker right）。</summary>
    ModelPickerIncreaseEffort,

    // ── 选择组件动作（基准 schema.ts:156-160 — 与 confirm:* 分离避免冲突）──

    /// <summary>select:next — 下一项（Select down / j / ctrl+n）。</summary>
    SelectNext,

    /// <summary>select:previous — 上一项（Select up / k / ctrl+p）。</summary>
    SelectPrevious,

    /// <summary>select:accept — 接受选中项（Select enter）。</summary>
    SelectAccept,

    /// <summary>select:cancel — 取消选择（Select escape）。</summary>
    SelectCancel,

    // ── 插件对话框动作（基准 schema.ts:161-163）──

    /// <summary>plugin:toggle — 切换插件启用（Plugin space）。</summary>
    PluginToggle,

    /// <summary>plugin:install — 安装插件（Plugin i）。</summary>
    PluginInstall,

    // ── 权限对话框动作（基准 schema.ts:164-165）──

    /// <summary>permission:toggleDebug — 切换权限调试信息（Confirmation ctrl+d）。</summary>
    PermissionToggleDebug,

    // ── 设置面板动作（基准 schema.ts:166-169）──

    /// <summary>settings:search — 进入搜索模式（Settings /）。</summary>
    SettingsSearch,

    /// <summary>settings:retry — 重试加载数据（Settings r）。</summary>
    SettingsRetry,

    /// <summary>settings:close — 保存并关闭（Settings enter）。</summary>
    SettingsClose,

    // ── 语音动作（基准 schema.ts:170-171）──

    /// <summary>voice:pushToTalk — 按住说话（VOICE_MODE 特性 space，默认表未绑定）。</summary>
    VoicePushToTalk,

    // ── 滚动动作（基准 defaultBindings.ts:195-213 Scroll 块，schema 清单未收编）──

    /// <summary>scroll:pageUp — 上翻一页（Scroll pageup）。</summary>
    ScrollPageUp,

    /// <summary>scroll:pageDown — 下翻一页（Scroll pagedown）。</summary>
    ScrollPageDown,

    /// <summary>scroll:lineUp — 上滚一行（基准 wheelup 鼠标滚轮 — w2 键模型无 wheel 事件，默认表转录跳过）。</summary>
    ScrollLineUp,

    /// <summary>scroll:lineDown — 下滚一行（基准 wheeldown — w2 键模型无 wheel 事件，默认表转录跳过）。</summary>
    ScrollLineDown,

    /// <summary>scroll:top — 滚到顶部（Scroll ctrl+home）。</summary>
    ScrollTop,

    /// <summary>scroll:bottom — 滚到底部（Scroll ctrl+end）。</summary>
    ScrollBottom,

    /// <summary>selection:copy — 复制选区（Scroll ctrl+shift+c；基准 cmd+c 因 w2 无 Super 修饰转录跳过）。</summary>
    SelectionCopy,

    // ── 消息动作条动作（基准 defaultBindings.ts:267-295 MessageActions 条件块，schema 清单未收编）──

    /// <summary>messageActions:prev — 上一个动作（MESSAGE_ACTIONS 特性 up / k，默认表未绑定）。</summary>
    MessageActionsPrev,

    /// <summary>messageActions:next — 下一个动作（MESSAGE_ACTIONS 特性 down / j，默认表未绑定）。</summary>
    MessageActionsNext,

    /// <summary>messageActions:top — 第一个动作（MESSAGE_ACTIONS 特性 meta+up / super+up，默认表未绑定）。</summary>
    MessageActionsTop,

    /// <summary>messageActions:bottom — 最后一个动作（MESSAGE_ACTIONS 特性 meta+down / super+down，默认表未绑定）。</summary>
    MessageActionsBottom,

    /// <summary>messageActions:prevUser — 上一条用户消息（MESSAGE_ACTIONS 特性 shift+up，默认表未绑定）。</summary>
    MessageActionsPrevUser,

    /// <summary>messageActions:nextUser — 下一条用户消息（MESSAGE_ACTIONS 特性 shift+down，默认表未绑定）。</summary>
    MessageActionsNextUser,

    /// <summary>messageActions:escape — 退出动作条（MESSAGE_ACTIONS 特性 escape，默认表未绑定）。</summary>
    MessageActionsEscape,

    /// <summary>messageActions:ctrlc — 动作条内 ctrl+c（MESSAGE_ACTIONS 特性 ctrl+c，默认表未绑定）。</summary>
    MessageActionsCtrlC,

    /// <summary>messageActions:enter — 确认动作（MESSAGE_ACTIONS 特性 enter，默认表未绑定）。</summary>
    MessageActionsEnter,

    /// <summary>messageActions:c — 动作 c（MESSAGE_ACTIONS 特性 c，默认表未绑定）。</summary>
    MessageActionsC,

    /// <summary>messageActions:p — 动作 p（MESSAGE_ACTIONS 特性 p，默认表未绑定）。</summary>
    MessageActionsP,
}
