namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位上下文 — 对齐基准 keybindings/schema.ts KEYBINDING_CONTEXTS（18 个合法上下文）
/// + defaultBindings.ts 额外使用的 Scroll/MessageActions 两块（基准默认表有、schema 清单未收编，此处一并收编，共 20 个）。
/// 用户 json 以小写名引用（"chat"），KeybindingNames 大小写不敏感解析；
/// Resolve/Match 未命中时回退 Global（对齐基准 resolver.ts activeContexts 含 Global 兜底语义）。
/// </summary>
public enum KeybindingContext
{
    /// <summary>Global — 任何焦点下都激活（基准 schema.ts:41 "Active everywhere, regardless of focus"）。</summary>
    Global,

    /// <summary>Chat — 聊天输入框聚焦时（基准 schema.ts:42 "When the chat input is focused"）。</summary>
    Chat,

    /// <summary>Autocomplete — 自动补全菜单可见时（基准 schema.ts:43）。</summary>
    Autocomplete,

    /// <summary>Settings — 设置菜单打开时（基准 schema.ts:50）。</summary>
    Settings,

    /// <summary>Confirmation — 确认/权限对话框显示时（基准 schema.ts:44）。</summary>
    Confirmation,

    /// <summary>Tabs — 标签导航激活时（基准 schema.ts:51）。</summary>
    Tabs,

    /// <summary>Transcript — 查看转录视图时（基准 schema.ts:46）。</summary>
    Transcript,

    /// <summary>HistorySearch — ctrl+r 历史搜索时（基准 schema.ts:47）。</summary>
    HistorySearch,

    /// <summary>Task — 前台任务/代理运行时（基准 schema.ts:48）。</summary>
    Task,

    /// <summary>ThemePicker — 主题选择器打开时（基准 schema.ts:49）。</summary>
    ThemePicker,

    /// <summary>Scroll — 视口滚动/选区复制（基准 defaultBindings.ts:195-213 块，schema 清单未收编）。</summary>
    Scroll,

    /// <summary>Help — 帮助浮层打开时（基准 schema.ts:45）。</summary>
    Help,

    /// <summary>Attachments — 选择对话框图片附件导航（基准 schema.ts:52 "Attachments"）。</summary>
    Attachments,

    /// <summary>Footer — 页脚指示器聚焦时（基准 schema.ts:53）。</summary>
    Footer,

    /// <summary>MessageSelector — 消息选择器（rewind）打开时（基准 schema.ts:54）。</summary>
    MessageSelector,

    /// <summary>DiffDialog — diff 对话框打开时（基准 schema.ts:55）。</summary>
    DiffDialog,

    /// <summary>ModelPicker — 模型选择器打开时（基准 schema.ts:56）。</summary>
    ModelPicker,

    /// <summary>Select — 选择/列表组件聚焦时（/model、/resume、权限提示等，基准 schema.ts:57）。</summary>
    Select,

    /// <summary>Plugin — 插件对话框打开时（基准 schema.ts:58）。</summary>
    Plugin,

    /// <summary>MessageActions — 消息动作条激活时（基准 defaultBindings.ts:267-295 条件块，schema 清单未收编）。</summary>
    MessageActions,
}
