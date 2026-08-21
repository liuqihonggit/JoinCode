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
- [x] **B2** GUI `StreamingEnabled` 开关无效（拨了没用）
  - **修复（2026-08-22）**：`MainViewModel.SendAsync` 消费该开关。顺带发现并修复更根本的缺陷：
    流式期间 assistant 消息从未加入 Messages（循环结束才 Add），逐 token 更新完全不可见。
  - 改动：① 助手消息先入列表作流式占位；② Content/Thinking 实时刷新受 `StreamingEnabled` 门控
    （关闭=完成后一次性填充）；③ 思考/工具卡片经 `InsertBeforeAssistant` 插到占位之前，
    保持"过程在前、回复在后"视觉顺序；④ 异常路径补 `IsStreaming=false` 清理（占位消息现在会留在列表中）。
  - 测试：`Send_WhileStreaming_AssistantMessageVisibleWithPartialContent`（流式中途可见）
    + `Send_WhenStreamingDisabled_AssistantContentHiddenUntilComplete`（关流式隐藏），
    门控流式假会话 + 事件驱动观察（JCC3010 禁止 Task.Delay 轮询）。
  - 位置：`app\JoinCodeGui\ViewModels\MainViewModel.cs` SendAsync
- [x] **B3** GUI `FontSize` 滑块无效（消息区硬编码13号字）
  - **修复（2026-08-22）**：`MainWindow.axaml:112` TextEditor `FontSize="13"` → `FontSize="{Binding FontSize}"`，
    设置面板滑块即时生效（VM 属性已持久化到 gui-preferences.json，无需额外改动）。
  - 测试：`MainWindowRegressionTests.FontSizeSlider_Change_UpdatesMessageTextEditor`（Headless 真窗口断言字号联动）
- [x] **B4** GUI `OnRemoveClick` 无 XAML 引用（消息删除不可达）→ **并入 G3 处理**
  - 查证（2026-08-22）：`CopyMessageCommand`/`ToggleThinkingCommand`/`RemoveMessageCommand`/`RegenerateCommand`
    四个命令均无 XAML 引用。根因：消息区是单个只读 TextEditor 平铺 AllMessagesText，
    不存在单条消息的视觉模板，✕/复制/折叠按钮无处安放。
  - 决策：不在 TextEditor 架构上打补丁，等 G3（Markdown/ItemsControl 单条消息模板重构）
    一并接线全部四命令。OnRemoveClick 保留为死代码至 G3 落地。
<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: B4并入G3而非单独修 -->
<!-- 原因: 死代码根因是渲染架构(TextEditor平铺)不支持单条消息交互,单独修需先建ItemsControl模板=G3本体 -->
<!-- 替代方案: 删除OnRemoveClick死代码(放弃,G3马上要用)-->
<!-- 验证: 编译通过 ✅ -->
- [x] **B5** TUI StatusBar 队列计数段死路径（`TerminalPainter.NotifyQueueChanged` 全项目零调用，"队列：N"永不更新）
  - **修复（2026-08-22）**：`TuiModeRunner.cs` 主循环快照 diff 处 `queuedCommands.OnQueueChanged(snapshot)`
    → `painter.NotifyQueueChanged(snapshot)`，广播给全部注册组件。
  - 测试：`QueueBroadcastTests`（StatusBarView 队列段显示 + painter 广播链路），TUI 全部 133 测试绿。
  - 备注：需先初始化 git 子模块 `libs/Terminal.Gui`、`libs/Editor`（本机此前未 init 导致 TUI 无法编译）
- [x] **B6** TUI F3"Stop"实为退出整个程序（无"中断但不退出"通道）
  - **修复（2026-08-22）**：工具栏标签本就承诺"停止当前任务"，实现却 `app.RequestStop()`。
  - 改动：① 每条命令独立链接 CTS（`currentQueryCts` StrongBox 容器在工具栏闭包与处理循环间共享）；
    ② Stop → 取消当前查询，输出"已请求停止当前生成"；空闲时提示"/exit 退出"；
    ③ OCE 捕获从 `break`（杀队列循环）改为输出后继续下一条命令；
    ④ finally 先置 null 再 Dispose + Stop 端 ObjectDisposedException 防护（消除竞态）。
  - 对齐 GUI Esc 停止语义；程序退出仍走 /exit。
  - 验证：编译通过 + TUI 全部 133 测试绿。
- [x] **B7** TUI 权限批准后重发原文导致上下文重复
  - **修复（2026-08-22）**：根因 `QueryEngine.QueryAsync`（core\execution\Brain\src\Query\Query2\TokenBudget\QueryEngine.cs:134）
    在管道执行前就 AddUserMessage，权限异常抛出时本轮消息已入历史，重发即二次追加。
  - 改动：① 命令执行前记录 `historySnapshotCount`；② 新增 `TuiModeRunner.RewindToSnapshot`
    （裁剪回快照点）；③ 批准分支先 Rewind 再重发——对齐 GUI `RewindLastTurnAsync` 语义。
  - 测试：`PermissionRewindTests`（裁剪生效 + 空增量 NoOp），TUI 135 测试全绿。
  - 备注：拒绝路径保持原样（GUI 拒绝也会保留错误结果入上下文）。
- [x] **B8**（存量缺陷）JoinCodeGui.Tests 模型列表测试失败
  - **修复（2026-08-22）**，两类根因：
    **① 稳定失败 7 个**：测试依赖本机 `~/.jcc/settings.json` 的 vendor 目录内容，
    空 DI/占位会话下 ModelConfigLoader 从未灌入 → map 为空。
    修复：测试密闭化——`CreateFedLoader()` fixture 灌入镜像生产配置的目录数据
    （DumpAllData/MultipleInstances/ModelSurface_* 三兄弟 + PlaceholderMode 注入 fed loader）。
    **② 套件级偶发失败**：三重根因全部修复——
    a. VM 构造硬编码 PhysicalFileSystem 的 ConfigurationService → 并行测试读写真实
       settings.json 互扰+污染用户配置；改为跟随 preferencesStore.FileSystem（测试 InMemory 全程密闭）
    b. Avalonia headless 跨类并行竞态（IFontManagerImpl 定位失败等）→ 新增
       `GuiUiSequential` 集合，7 个 AvaloniaFact 测试类串行执行
    c. ToggleThemeVm 裸构造异步读真实 theme 键晚到覆盖断言 → 改 InMemory 构造
  - 验证：GUI 套件 **5 连跑全绿（323×5）**；Host.Tests 985 全绿。
  - ⚠️ 遗留提醒：修复前的测试曾向真实 `~/.jcc/settings.json` 写入 profile/theme 键，建议人工检查该文件。

## 三、修 GUI 阶段（补 TUI 有的能力）

- [x] **G1** 斜杠命令真实执行链路（最大缺口）：InputText 以 `/` 开头时路由到命令系统而非聊天
  - **完成（2026-08-22）**：
    ① 新增共享执行器 `app\JoinCode\Cli\Commands\SlashCommandRunner.cs`（解析→CmdMap 路由→
    ChatCommandContext 构造→Console.Out 捕获），UI 差异经回调注入（Confirm/Prompt/ClearScreen 等）；
    ② TuiModeRunner 重构复用 runner，删除自带的 BuildCommandServices/HandleSlashCommandAsync 重复实现
    （消除两套实现）；
    ③ `IJccChatSession` 新增 `ExecuteSlashCommandAsync`；JccChatSession 委托 runner；Placeholder 返回提示文案；
    ④ MainViewModel.SendAsync 拦截 `/` 前缀 → 系统消息回显"⚙️ 命令 + 输出"，不进聊天流。
  - 测试：`Send_WithSlashInput_RoutesToCommandExecutorNotChat`（红→绿：命令路由到执行器、
    StreamAsync 零调用、输出回显）。GUI 324 全绿、TUI 135 全绿、Host.Tests 985 全绿。
  - ⚠️ 已知边界：需要 Confirm 确认的命令在 GUI 中默认拒绝（回调未接弹窗）；/exit 的 onExitRequested
    在 GUI 中未接窗口关闭——两者留待 G 阶段后续按需补齐。
- [x] **G2** 真实 token 用量：消费流事件中的 Usage，替换字符估算
  - **完成（2026-08-22）**：`ChatStreamEvent` 的 Complete/Done 事件已携带 `TokenUsage`（此前 VM
    未处理该事件类型直接丢弃）。新增 `TokenUsageText` 属性，SendAsync 累加 `evt.Usage.TotalTokens`，
    状态栏新增一列显示 "Token:N"（千位分隔）；引擎未上报时显示空串（保留输入框字符估算不变）。
  - 测试：`Send_WithUsageInDoneEvent_ShowsRealTokenCount`（红→绿）。GUI 325 全绿。
  - 位置：`MainViewModel.cs` SendAsync + `MainWindow.axaml` 状态栏
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

### 修 Bug 阶段小结（2026-08-22 完成）

| 项 | 结果 | 提交 |
|----|------|------|
| B1 关窗兜底 | 查证为误报，加契约测试钉死 | dfcc27d2f |
| B2 StreamingEnabled + 流式可见性 | 已修（占位先入列表+门控刷新+插入保序） | 81d2fc130 |
| B3 FontSize 硬编码 | 已修（Binding） | b79e6acf2 |
| B4 单条消息命令孤立 | 并入 G3（TextEditor 平铺渲染不支持单条交互） | 3cd9bc05c |
| B5 TUI 队列计数死路径 | 已修（改经 painter 广播） | 33f99af13 |
| B6 TUI Stop 退出程序 | 已修（每命令 CTS，对齐 GUI Esc 语义） | 417f4f247 |
| B7 TUI 权限重发重复上下文 | 已修（RewindToSnapshot 对齐 GUI Rewind） | b3066ef42 |
| B8 存量 models.json 缺陷+套件偶发 | 已修（密闭化 fixture + InMemory 配置 + 串行集合 + theme 竞态） | 见下次提交 |

验证：Host.Tests 985 全绿；JoinCodeGui.Tests **323 全绿 × 5 连跑**（此前基线 7 稳定失败+随机漂移失败）。
环境备注：本机需 `git submodule update --init` 初始化 libs/Terminal.Gui 与 libs/Editor 才能编译 TUI；
init 会把 .gitmodules URL 改写为 gitee 回退源，提交前需 `git checkout -- .gitmodules` 还原。

<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: B2顺带修复流式输出不可见缺陷(assistant循环后才Add导致逐token更新无视觉效果) -->
<!-- 原因: 两缺陷同根因,占位消息先入列表是StreamingEnabled生效的前提 -->
<!-- 替代方案: 仅门控Content赋值(放弃,流式依旧不可见)-->
<!-- 验证: 编译通过,新增2测试绿,GUI套件无回归 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: B5/B6/B7采用"机制单测+一行接线"模式而非全链路红测试 -->
<!-- 原因: TuiModeRunner主循环依赖Terminal.Gui Application.Run,无法headless单测;机制层(painter广播/裁剪语义)已钉死 -->
<!-- 替代方案: 重构ProcessQueueAsync为可注入(放弃,超出bug修复范围,留待T阶段)-->
<!-- 验证: 编译通过,TUI 135测试全绿,Host.Tests 985全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: B8采用"测试密闭化"而非"改生产适配测试" -->
<!-- 原因: 测试依赖本机settings.json违反可重复性原则;VM硬编码物理fs是真实缺陷(污染用户配置+并行互扰),顺带修复 -->
<!-- 替代方案: CI预置固定settings.json（放弃,不解决本机开发体验且掩盖设计问题）-->
<!-- 验证: GUI套件323×5连跑全绿,Host.Tests 985全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 -->
<!-- 决策: 分三阶段执行（bug→GUI→TUI），每项独立编译+测试+提交 -->
<!-- 原因: B1 是权限安全缺陷优先级最高；GUI 补斜杠执行是用户可感知的最大行为差异 -->
<!-- 替代方案: 双向并行子智能体（放弃，避免同仓库冲突且无法逐项验证）-->
