# GUI 交互升级六项任务（2026-08-26）

> 来源：用户验收反馈。状态：待实施（顺序与细节经用户确认后执行）

## 任务清单

### F1 走马灯状态条（学习 opencode）
- AI 正常对话中（连接未断）→ 左下角显示走马灯条，替代现有"就绪"绿点+文字
- 内容：滚动显示当前活动摘要（动词/当前工具/子代理动态/耗时/token）
- 断线/错误时保留红色告警态
- 涉及：`MainWindow.axaml` 状态栏区、`GlobalRunStatusViewModel`（增加 MarqueeText 计算）、新 MarqueeTextBlock 控件（或 DoubleAnimation 平移实现）

### F2 双击 ESC 终止对话
- 双击 ESC（~600ms 内两次）→ 触发现有 StopCommand（`_sendCts.Cancel()` 仅断对话网络）
- 遥测网络独立服务，天然不受影响 ✓
- 单次 ESC：预留（可配置为无操作/清空输入框）
- 涉及：`InputBarView.axaml.cs` KeyDown、`MainViewModel.StopCommand`

### F3 Enter=换行 / Ctrl+Enter=发送 + 快捷键面板
- 默认改为 Enter 换行、Ctrl+Enter 发送（**需用户确认默认值方向**）
- 输入栏 placeholder 提示当前组合键
- 新增快捷键设置面板（SettingsPanel 内新 Tab 或弹窗）：动作列表 × 可重绑按键
- 持久化到 GuiPreferencesStore；首版可绑定动作：发送/换行/停止(双ESC)/清空输入/聚焦输入
- 涉及：`InputBarView`、`SettingsPanelView`、`GuiPreferences`、新 `ShortcutsPanelView`

### F4 与子代理对话（@提及）接入 GUI —— 已确认现状缺失
CLI 已有两条规则（ReplLoopStep），GUI 均未接：
1. `@agentName 消息` → `FindAgentIdByNameAsync` → `ForwardUserInputToAgentAsync`
2. 处理中且恰好 1 个运行代理时，普通消息自动转发给它
GUI 实施：SendMessageCoreAsync 前置同样两规则 + 输入栏 @ 补全（复用运行列表）；转发成功在消息流插入系统回显卡
- 涉及：`IJccChatSession`(+)、`JccChatSession`、`InputBarView` 补全、`MainViewModel`

### F5 子代理 worktree 右键菜单
- AgentStarted 事件补 WorktreePath 字段（引擎 AgentToolContext/ForkResult 已携带）
- 运行卡片右键 ContextMenu：「在资源管理器中打开」→ `Process.Start("explorer.exe", path)`
- 无 worktree 的 agent 不显示该项
- 涉及：`ChatStreamEvent`(+字段)、中间件透传、`AgentRunVm`、`MainWindow.axaml` ContextMenu

### F6 今日 commit 的 GUI 截图美观分析
- 启动 GUI（Placeholder 会话即可渲染全部新组件）→ PowerShell 截屏
- 对运行卡片/状态条/pill 面板/回放窗逐一截图 → 美观度分析报告（间距/对比度/信息密度）
- 风险：沙箱截屏可行性未知，失败则改用 Avalonia headless 渲染导出 PNG（RenderTargetBitmap）

## 建议实施顺序
F4（功能缺口最大）→ F2 → F3 → F1 → F5 → F6（截图收尾验证全链路视觉）

## 待用户确认
见对话提问。
