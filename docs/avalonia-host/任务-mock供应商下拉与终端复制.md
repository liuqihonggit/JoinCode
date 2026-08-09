# 任务：Mock 供应商下拉 + 终端式整块复制

## 背景

用户需求（2026-08-09 演示后反馈）：
1. **把 mock 引擎加到候选列表，提供供应商下拉（供应商 map）**——随时知道当前连的是 Mock 服务器还是真实环境，避免误把演示当真实接入。
2. **AI 输出无法像终端一样整块选择/复制**——只能选中文字块；diff 在 Border 容器里选择被阻断；希望穿透全部（含名称标签、工具名、diff 行）复制。

## 决策

- **供应商下拉**：`ConnectionOptions` 含两项——🧪 Mock 引擎（演示）+ ☁️ 真实供应商（当前配置 provider）。默认选中当前 session 对应项。
- **切换机制**：`MainViewModel` 缓存注入的真实 `JccChatSession`（`_realSession`）与懒创建的 `PlaceholderChatSession`（`_mockSession`），`_session` 可变，切换时换引用并刷新模型列表/推理力度/状态栏。不销毁真实会话（避免反复重建 DI）。
- **复制整块**：新增 `ChatUiMessage.CopyAllText`（终端式纯文本：`[角色 · 时间]` + 思考 + 工具名/参数/结果 + diff 行 + 正文）；`CopyMessage` 复制该文本经 `CopiedMessageCopy` 交给 View 写剪贴板。
- **选择穿透**：DiffViewer 全部 `TextBlock` 改 `SelectableTextBlock`，消息标签（RoleLabel/KindLabel/ToolName/ToolArguments/ToolResultText）改 `SelectableTextBlock`，使鼠标可逐块选择；整块复制用 ⧉ 按钮一键完成。

## 实施计划

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 记录任务文档 | ✅ |
| 2 | 红测试：ConnectionOptions 含 Mock + 真实供应商 | ✅ |
| 3 | 红测试：切换 Mock/真实会话状态与模型刷新 | ✅ |
| 4 | 实现：ConnectionOptionItem + MainViewModel 连接切换 | ✅ |
| 5 | 实现：MainWindow.axaml 供应商下拉 + Mock 徽标 | ✅ |
| 6 | 红测试：ChatUiMessage.CopyAllText（含标签/diff） | ✅ |
| 7 | 实现：CopyMessage 复制整块 + DiffViewer/标签 SelectableTextBlock | ✅ |
| 8 | 编译 + 全量测试 + 提交 | ✅（GUI 148 全绿，提交见 git log） |
| 9 | GUI 冒烟验证 | ⏳ |

<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: 连接切换采用"双会话缓存 + _session 可变引用"，不销毁真实会话 -->
<!-- 原因: 避免切换时反复重建 DI 会话，且真实会话内的流式/工具状态可保留 -->
<!-- 替代方案: 每次切换重建 session（复杂度高，易丢状态，不采用）-->
<!-- 验证: 编译通过，GUI 单测 148 全绿（含新增 8 项）✅ -->

<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: CopyAllText 用纯 StringBuilder 手写终端式文本，不加第三方序列化 -->
<!-- 原因: 需精确控制角色标签/时间/思考/工具/diff 行的文本顺序，序列化器无法表达展示顺序 -->
<!-- 替代方案: 复用 ExportSessionText 扩展（不含思考/工具/diff，不满足需求）-->
<!-- 验证: CopyAllText 5 项单测全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: DiffViewer/消息标签改 SelectableTextBlock 实现逐块选择穿透 -->
<!-- 原因: Avalonia 的 SelectableTextBlock 支持鼠标划选且可单独设 SelectionBrush，Border 容器不再阻断选择 -->
<!-- 替代方案: 全局统一选择层（复杂度高，收益低，不采用）-->
<!-- 验证: 编译通过，Avalonia 11.3.3 属性名 SelectionBrush 验证无误 ✅ -->

## 涉及文件

- `app/JoinCodeGui/ViewModels/MainViewModel.cs`：连接切换 + CopyMessage 整块复制
- `app/JoinCodeGui/ViewModels/ChatUiMessage.cs`：CopyAllText 属性
- `app/JoinCodeGui/ViewModels/ConnectionOptionItem.cs`（新建）：连接下拉项
- `app/JoinCodeGui/Views/MainWindow.axaml`：供应商下拉 + 徽标 + SelectableTextBlock
- `app/JoinCodeGui/Markdown/DiffViewer.cs`：TextBlock → SelectableTextBlock
- `tests/Unit/JoinCodeGui.Tests/ViewModels/MainViewModelTests.cs`：红测试
