# TUI/GUI 双端对齐任务清单

> 生成时间：2026-08-22
> 目标：丰富 TUI 与 GUI，两边逻辑一致。流程：修 bug → 修 GUI → 修 TUI，每完成一项打勾一项。
> 架构前提：TUI 走 `IQueryEngine.QueryAsync`（ChunkFormatter 文本流）；GUI 走 `IJccChatSession.StreamAsync`（ChatStreamEvent 门面）。

---

## 一、现状对照总表

### 消息展示

| 功能 | TUI | GUI | 差异 |
|------|-----|-----|------|
| 用户消息回显 | ✅ TuiModeRunner.cs:281 | ✅ MainViewModel.cs:1218 | GUI 带角色+时间戳 |
| AI 流式回复 | ✅ TuiModeRunner.cs:295 | ✅ MainViewModel.cs:1238 | — |
| 工具调用卡片 | ⚠️ 纯文本行（截断200字） | ✅ 结构化卡片+进度+错误态 | TUI 简陋 |
| 思考过程 | ⚠️ 纯文本不可折叠 | ✅ 可折叠气泡 | TUI 简陋 |
| Markdown 渲染 | ❌ | ⚠️ 已实现未接线（约600行死代码） | 两边实际都是纯文本 |
| Diff 渲染 | ❌ | ✅ +/-红绿高亮 | GUI 独有 |
| 输出环形缓冲 | ✅ 2048行淘汰+100ms节流 | ❌ 无上限 | GUI 长会话内存风险 |
| 自动滚动 | ⚠️ 强制贴底 | ✅ 上滑暂停+回底浮钮 | TUI 无法上翻阅读 |
| 消息搜索过滤 | ❌ | ✅ SearchText 过滤 | GUI 独有 |
| 单条消息复制/会话导出 | ⚠️ Editor 自带 | ✅ CopyMessage/CopySessionExport+toast | — |

### 输入体验

| 功能 | TUI | GUI | 差异 |
|------|-----|-----|------|
| 多行输入 | ✅ Ctrl+Enter发送 | ✅ Enter发送 | ⚠️ 键位相反 |
| 历史命令导航 | ✅ Ctrl+Up/Down（容量20） | ✅ Up/Down（无上限） | 触发键不同 |
| Tab 斜杠补全 | ⚠️ 简版前缀替换 | ✅ 四模式弹窗（命令/参数/@文件/#工具） | — |
| **斜杠命令执行** | ✅ CmdMap 完整链路（TuiModeRunner.cs:344-438） | ❌ **当聊天发给LLM** | **最大缺陷** |
| 字符计数超限警示 | ❌ | ✅ MaxTokens×3 上限标红 | GUI 独有 |

### 状态显示

| 功能 | TUI | GUI | 差异 |
|------|-----|-----|------|
| 模型名 | ✅ 环境变量一次性读取 | ✅ 下拉可切换+热重载 | TUI 运行中不可变 |
| **真实 token 用量** | ✅ chunk.Usage.TotalTokens 累加（TuiModeRunner.cs:303） | ❌ 仅字符/4估算 | GUI 需接 Usage |
| 耗时 | ✅ 全局计时器 | ✅ 单工具卡片计时 | 口径不同 |
| spinner 忙碌指示 | ⚠️ 静态"● Running"永不变更 | ✅ 三态闪烁动画 | — |
| 命令队列预览 | ✅ QueuedCommandsView | ❌ 阻塞模型无队列 | 架构差异 |

### 会话管理

| 功能 | TUI | GUI | 差异 |
|------|-----|-----|------|
| 新建会话 | ⚠️ 内存级清屏（F1） | ✅ SessionItem+引擎隔离 | — |
| **持久化/resume** | ❌ 退出即失 | ✅ 共享 ~/.jcc/sessions + LoadHistoryAsync 灌入引擎 | **TUI 最大缺口** |
| 删除/重命名会话 | ❌ | ✅ 内联编辑+自动命名 | GUI 独有 |

### 权限确认

| 功能 | TUI | GUI | 差异 |
|------|-----|-----|------|
| 对话框 | ✅ 内嵌面板2选项 | ✅ 模态3选项 | GUI 多"始终允许" |
| 批准语义 | 一律5分钟临时 | 本次5分钟/始终24小时 | — |
| 重放方式 | ⚠️ 重发原文（可能重复上下文） | ✅ RewindLastTurnAsync 去重 | — |
| 重试上限 | ⚠️ 无上限循环 | ✅ 最多3次 | — |

### AskUserQuestion / 子代理 / 设置

| 功能 | TUI | GUI |
|------|-----|-----|
| AskUserQuestion | ❌ Prompt 回调返回 null | ✅ 完整（单/多选+自由输入+线程封送） |
| 子代理面板 | ⚠️ 五件套基建齐全但零调用（死代码） | ❌ 无对应物 |
| 设置面板 | ❌ 仅打印2行文本 | ✅ 温度/MaxTokens/Effort/SystemPrompt/字号 |
| 供应商/模型切换 | ❌ 固定启动配置 | ✅ models.json 驱动+持久化 |
| 配置热重载 | ❌ | ✅ FileSystemWatcher 1s防抖 |
| 主题 | ❌ | ✅ 深/浅切换+与CLI双向同步 |
| Mock降级 | ❌ | ✅ PlaceholderChatSession+失败回退 |

---

## 二、修 Bug 阶段（最高优先级）

- [x] **B1** 权限弹窗关窗=null 落入"批准"分支 → 应兜底为拒绝
  - **结论（2026-08-22）：误报，无需改代码**。查证 Avalonia 11.x `Window.ShowCore` 源码：
    `tcs.SetResult((TResult)(_dialogResult ?? default(TResult)!))` — 未点按钮直接关窗返回 `default(TResult)`，
    而 `PermissionConfirmationDecision` 枚举首值为 `Deny`(=0)，故"关窗=拒绝"成立。
  - **风险**：安全性依赖"枚举首值必须是 Deny"的隐式契约。已加 2 个回归测试钉死：
    `PermissionDialogTests.CloseWindowWithoutClicking_ReturnsDeny`（Headless 关窗验证运行时行为）
    + `DefaultDecision_MustBeDeny`（纯枚举层面阻断重排事故）。若有人调整枚举顺序立即红灯。
  - 位置：`app\JoinCodeGui\Views\MainWindow.axaml.cs:140`、`Hosting\PermissionConfirmation.cs:19`
- [ ] **B2** GUI `StreamingEnabled` 开关无效（拨了没用）
  - 位置：`SettingsPanelView.axaml` + `MainViewModel.SendAsync` 从不读取 → 接线或移除
- [ ] **B3** GUI `FontSize` 滑块无效（消息区硬编码13号字）
  - 位置：`MainWindow.axaml:112` TextEditor FontSize 未绑定 prefs
- [ ] **B4** GUI `OnRemoveClick` 无 XAML 引用（消息删除不可达）
  - 位置：`MainWindow.axaml.cs:405-411`
- [ ] **B5** TUI StatusBar 队列计数段死路径（`TerminalPainter.NotifyQueueChanged` 全项目零调用，"队列：N"永不更新）
  - 位置：`TerminalPainter.cs:66-78`、`StatusBarView.cs:92-96`；主循环快照处接线或删除该段
- [ ] **B6** TUI F3"Stop"实为退出整个程序（无"中断但不退出"通道）
  - 位置：`TuiModeRunner.cs:113-114,177`；应改为取消当前 CTS
- [ ] **B7** TUI 权限批准后重发原文导致上下文重复
  - 位置：`TuiModeRunner.cs:326`；对齐 GUI 的 Rewind 语义或改为工具级批准后继续

## 三、修 GUI 阶段（补 TUI 有的能力）

- [ ] **G1** 斜杠命令真实执行链路（最大缺口）：InputText 以 `/` 开头时路由到命令系统而非聊天
  - 参考 TUI：commandRegistry.Parse → cmdMap.ResolveAsync → ChatCommandContext 回调 → Console.Out 捕获回显
  - GUI 侧需在 `MainViewModel.SendAsync` 入口拦截 `/` 前缀，经 IJccChatSession 新增 ExecuteCommandAsync 门面
- [ ] **G2** 真实 token 用量：消费流事件中的 Usage，替换字符估算
  - 流事件已有数据源（对齐 TUI TuiModeRunner.cs:303-309），显示到底部状态栏
- [ ] **G3** 接线 Markdown 渲染：MarkdownView/MarkdownParser/DiffViewer 已实现未引用，接入消息区替代纯文本
- [ ] **G4** 输出环形缓冲/内存防护：ObservableCollection 无上限，长会话需淘汰策略
- [ ] **G5** 命令队列预览（可选）：GUI 阻塞模型下价值减弱，评估是否需要

## 四、修 TUI 阶段（补 GUI 有的能力）

- [ ] **T1** 会话持久化/resume：共享 `~/.jcc/sessions/*.json`，列表/恢复灌入引擎（对齐 GUI GuiSessionStore + LoadHistoryAsync 语义）
- [ ] **T2** AskUserQuestion：Prompt 回调从返回 null 改为终端交互问答（单选/多选/自由输入）
- [ ] **T3** 权限三档决策："始终允许"=24小时会话级（对齐 JccChatSession.cs:26-29 常量语义）；重试上限3次
- [ ] **T4** 设置能力：至少支持温度/MaxTokens/Effort 写回 ExecutionSettingsProvider（对齐 GUI WriteBackTemperatureAndMaxTokens）
- [ ] **T5** 供应商/模型运行时切换（可选：文件驱动界面规则7的 TUI 形态）

## 五、清理决策清单（做之前先问用户）

- [ ] **C1** TUI 死代码：SubAgentCardManager（零引用）、AgentPanesView.RegisterAgent（零调用）、MessageStyle/ColorMapper 样式体系（渲染路径不消费）
  - 处置选项：删除 vs 接线激活（G3/T2 可能用到样式体系，暂缓删除）
- [ ] **C2** GUI Markdown 六件套若 G3 不做则归档 `.xxx/`

---

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: 分三阶段执行（bug→GUI→TUI），每项独立编译+测试+提交 -->
<!-- 原因: B1 是权限安全缺陷优先级最高；GUI 补斜杠执行是用户可感知的最大行为差异 -->
<!-- 替代方案: 双向并行子智能体（放弃，避免同仓库冲突且无法逐项验证）-->
