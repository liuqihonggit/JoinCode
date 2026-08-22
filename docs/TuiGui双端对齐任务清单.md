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
- [x] **G3** 接线 Markdown 渲染：MarkdownView/MarkdownParser/DiffViewer 已实现未引用，接入消息区替代纯文本
  - **完成（2026-08-22）**：消息区从单个只读 TextEditor 重构为 ItemsControl 单条消息卡片模板化渲染：
    ① 正文非流式时经 MarkdownView 渲染（标题/代码块/列表/表格/引用），流式期间降级纯文本避免逐 token 重建控件树；
    ② 工具启动卡片显示名称+等宽参数，工具结果显示文本+DiffViewer（StructuredPatch 驱动）；
    ③ 思考气泡折叠提示接 ToggleThinkingCommand；
    ④ **四孤立命令全部激活**（B4 兑现）：📋 CopyMessage / ✕ RemoveMessage / 🧠 ToggleThinking /
    ↻ Regenerate（TopBar 原有）；
    ⑤ 搜索直接过滤 ItemsSource=FilteredMessages；字号滑块联动 BaseFontSize；自动滚动改 ScrollViewer.ScrollToEnd。
  - 移除死路径：xshd 高亮加载、OnRemoveClick 孤儿方法、AllMessagesText 显示分支（保留导出/复制用途）。
  - 测试：删除按钮移除消息 + 复制按钮反馈态 + Markdown 冒烟 3 个新增 headless 测试，
    FontSizeSlider/Constructor 回归测试改指新控件。GUI 328 全绿。
  - 位置：`MainWindow.axaml(.cs)`
- [x] **G4** 输出环形缓冲/内存防护：ObservableCollection 无上限，长会话需淘汰策略
  - **完成（2026-08-22）**：`MainViewModel.MaxVisibleMessages = 500` 硬上限，
    OnMessagesChanged 内超限 `RemoveAt(0)` 裁剪最旧（Remove 事件重入同步维护助手计数器，无双重递减）。
    仅裁剪 UI 显示层；引擎上下文由 /compact 管理；TUI 端 OutputView 已有 2048 行环形缓冲无需改动。
  - 测试：超限裁剪 + 计数器一致性 2 个新增测试。GUI 331 全绿。位置：`MainViewModel.cs`
- [x] **G5** 命令队列预览（可选）— **决策：不做（N/A）**
  - GUI 为阻塞模型（IsBusy 挡发送、无排队队列），不存在"待执行命令"可预览；
    TUI 的 QueuedCommandsView 已覆盖其自身队列场景。强行加 UI = 加法思维反模式。

## 四、修 TUI 阶段（补 GUI 有的能力）

- [x] **T1** 会话持久化/resume：共享 `~/.jcc/sessions/*.json`，列表/恢复灌入引擎（对齐 GUI GuiSessionStore + LoadHistoryAsync 语义）
  - **完成（2026-08-22）**：存储层复用 CLI ResumeCommand 天然读写 `~/.jcc/sessions/*.json`
    （GUI/TUI/CLI 三端同一命令链路 `/resume` → `LoadSessionMessagesAsync` 灌引擎，零新存储代码）。
    补齐的缺口是**命令后 UI 历史同步**：
    ① TUI `SyncHistoryFromEngine(MessageList, ApiMessageRecord[])` — ReplaceAll 原子重建本地 chatHistory
    （角色 FromValue 映射，未识别回退 Tool），HandleSlashCommandAsync 执行成功后经 painter.Invoke 接线；
    ② GUI `ReloadMessagesFromEngineAsync(echo)` — 重读 GetMessagesAsync 重建消息列表，
    ⚙️ 命令回显保留末尾（角色回退 User）。/resume /clear /compact 全部受益。
  - 测试：HistorySyncTests 3 个（映射/清空/未知角色）+ GUI 重读测试 1 个。
    TUI 138 全绿、GUI 331 全绿。
  - 位置：`TuiModeRunner.cs` + `MainViewModel.cs`
- [x] **T2** AskUserQuestion：Prompt 回调从返回 null 改为终端交互问答（单选/多选/自由输入）
  - **完成（2026-08-22）**：根因是 TUI DI 不含 CliModule（Program.cs 注释明示），ask_user_question 工具
    落到 Core 层 Mock InteractiveService——用户从未被真正提问，AI 拿到假答案。
  - 修复（对齐 GUI GuiInteractionModule 覆盖模式）：
    ① `TuiInteractionModule`(Order=80) 注册 TerminalGuiInteractiveService 覆盖 Mock；
    ② `TerminalGuiInteractiveService`：校验语义对齐 CLI（空问题/最多4问/选项2-4/重复标签），
    经 painter.Invoke 切 TUI 主循环弹对话框，未绑定 UI 时显式失败（绝不静默替用户作答）；
    ③ `AskUserDialogView`：Header/问题/选项渲染 + TextField 序号输入 + 确定/取消按钮，
    无效输入不关窗重试提示；无选项时自由输入；
    ④ `AskUserSelectionParser` 纯函数解析（1-based、0=取消、多选逗号去重）。
  - 测试：Parser 6 + DialogView 6 + Service 校验 6 = 18 个新增。TUI 156 全绿。
  - 冒烟：jcctui --await 启动正常，extraModules 注入未破坏 DI（诊断日志 session created → app.Run）。
  - 位置：`JoinCodeTui/{Interaction,Tui/Tui,Hosting}` + `Program.cs`
- [x] **T3** 权限三档决策："始终允许"=24小时会话级（对齐 JccChatSession.cs:26-29 常量语义）；重试上限3次
  - **完成（2026-08-22）**：
    ① `PermissionDialogView` 升级三档按钮：允许一次(y) / **始终允许(a)** / 拒绝(n)；
    ② 新增 `ShowWithDecisionAsync` 返回 Abstractions 层 `PermissionConfirmAction`
    （TUI 不引用 GUI 专属 PermissionConfirmationDecision），旧 `ShowAsync`(bool) 保留供 slash confirm 复用；
    ③ `GetApprovalDuration` 映射：Allow=5min / AlwaysAllow=24h / Deny=Zero（对齐 GUI 常量）；
    ④ `MaxPermissionRetries=3` 重试上限——超限报错终止本轮，不再无限弹窗循环。
  - 测试：映射 4 + 对话框三档 4 = 8 个新增。TUI 164 全绿。
  - 位置：`PermissionDialogView.cs` + `TuiModeRunner.cs`
- [x] **T4** 设置能力：至少支持温度/MaxTokens/Effort 写回 ExecutionSettingsProvider（对齐 GUI WriteBackTemperatureAndMaxTokens）
  - **完成（2026-08-22）**：
    ① Effort 零新增——CLI `/effort` 已写 `settingsProvider.EffortLevel`，TUI 经共享 SlashCommandRunner 免费获得；
    ② 温度/MaxTokens 新增共享 ChatCommand **`/sampling [温度] [最大Token|unset]`**
    （`Commands/ai/Model/SamplingCommand.cs`）：无参=查询当前值、unset=清除覆盖回退引擎默认、
    温度校验 0-2、只给温度不动 MaxTokens（对齐 GUI 滑块独立语义）；写回 `IExecutionSettingsProvider`
    内存生效，ChatOptionsFactory 下次创建即用（对齐 CLI 不持久化约定）。
  - 三端可达：TUI/GUI 走共享 runner；CLI 注册表自动含新命令。ChatCommandName 枚举加 Sampling
  （源码生成器全量重建）。
  - 测试：8 个新增（元数据+写回/查询/清除/无效值）。Host.Tests 全量 **1022 全绿**。
  - 位置：`SamplingCommand.cs` + `ChatCommandName.cs`
- [x] **T5** 供应商/模型运行时切换（可选：文件驱动界面规则7的 TUI 形态）
  - **完成（2026-08-22）**：
    ① 模型切换零新增——CLI `/model [id|default|info]` 已完整实现（内存 SetPrimaryModel +
    settings.json 持久化 + fast-mode 自动关 + effort 自动降级），TUI 经共享 runner 免费获得；
    ② 供应商切换新增共享 ChatCommand **`/vendor [名称|list]`**（`VendorCommand.cs`）：
    无参=列出全部（VendorKind 枚举唯一数据源，规则7）+ 当前标记；切换对齐 GUI
    `SetVendorAsync` 语义——WorkflowConfig.Provider.Vendor 内存切换 + 默认模型跟随
    （IModelCatalog.GetDefaultModelForProvider + fastMode.SetPrimaryModel）+
    settings.json `profile` 键持久化；无效名拒绝改动；同供应商 no-op。
    三端可达：CLI 注册表 / TUI+GUI 经共享 SlashCommandRunner。
  - 测试：6 个新增（元数据/列表/切换+持久化配置/无效拒绝/同值no-op）。
    Host.Tests 全量 **1028 全绿**、GUI 331 全绿。
  - 位置：`VendorCommand.cs` + `ChatCommandName.cs`

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

<!-- 🤖 Auto Decision: 2026-08-22 (T1+G4) -->
<!-- 决策1: T1 不新建 TUI 存储层，复用 CLI ResumeCommand 读写 ~/.jcc/sessions + 补 UI 历史同步钩子 -->
<!-- 原因1: 三端同一命令链路=消除两套实现的正解；GUI 的 GuiSessionStore 是 GUI 内会话列表管理，语义不同不合并 -->
<!-- 替代方案1: 抽取共享 SessionStore 到 app/JoinCode（放弃：大迁移风险晚节不保，格式已天然互通）-->
<!-- 决策2: G4 上限裁剪放 CollectionChanged 处理器而非每个 Add 调用点 -->
<!-- 原因2: 单一收口点覆盖全部路径（发送/恢复/导入），RemoveAt 重入同步维护计数器 -->
<!-- 替代方案2: 封装 AppendMessage 统一入口（放弃：需改 10+ 调用点，收益相同）-->
<!-- 验证: GUI 331 全绿 + TUI 138 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T2) -->
<!-- 决策: AskUserQuestion 走独立 TuiInteractionModule+对话框视图,而非复用 PermissionDialogView 或 CLI TerminalInteractiveService -->
<!-- 原因: TUI DI 不含 CliModule(Mock 根因);Console.ReadLine 会破坏 Terminal.Gui 全屏渲染;Permission 语义是布尔确认不承载多选 -->
<!-- 替代方案: 给 SlashCommandRunner 的 prompt 回调做终端问答(放弃: prompt 是 /commit 自由输入回调,与结构化问答语义不同)-->
<!-- 验证: Parser/DialogView/Service 共18测试全绿 + jcctui 冒烟 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T3) -->
<!-- 决策: TUI 权限决策枚举用 Abstractions 层 PermissionConfirmAction,而非 GUI 的 PermissionConfirmationDecision -->
<!-- 原因: JoinCodeTui 不引用 JoinCodeGui,GUI 枚举不可达;两枚举三值语义相同,Abstractions 版是正确共享层 -->
<!-- 替代方案: 把 GUI 枚举上移 Abstractions（放弃: 动 GUI 公共 API 面,收益仅为命名统一）-->
<!-- 验证: 映射+对话框8测试全绿,TUI 164 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T4) -->
<!-- 决策: 温度/MaxTokens 做成共享 ChatCommand /sampling 而非 TUI 专属设置面板 -->
<!-- 原因: 三端同享一份实现(CLI注册表/TUI与GUI经共享runner),符合消除两套实现原则;GUI 面板已有 session 方法路径不冲突 -->
<!-- 替代方案: TUI FooterTab Settings 页签做交互面板(放弃: 工作量大且只服务 TUI 一端,后续可选补充)-->
<!-- 验证: SamplingCommandTests 8绿,Host.Tests 全量 1022 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T5) -->
<!-- 决策: /vendor 列表数据源用 Enum.GetValues<VendorKind>() 而非新建 providers 配置文件节点 -->
<!-- 原因: VendorKind 枚举+[EnumValue] 已是供应商唯一数据源(规则3枚举唯一数据源),models.json 的 providers 节点驱动的是模型列表非供应商清单 -->
<!-- 替代方案: 读 models.json providers 键（放弃: 与枚举双源冲突，违反单一数据源原则）-->
<!-- 决策2: T5 整体走"共享 ChatCommand"路线而非 TUI 专属 UI -->
<!-- 验证: VendorCommandTests 6绿 + 全量 1028/331 全绿 ✅ -->

---

## 任务完成总结（2026-08-22）

全部任务 B1-B8 / G1-G5 / T1-T5 完成。本轮（PR #125 合并后）新增提交：
T1+G4（615e50062）→ T2（0d234dc11）→ T3（74e58a8a4）→ T4（e74984f02）→ T5。

最终测试基线：Host.Tests **1028** 全绿、JoinCodeGui.Tests **331** 全绿。
架构收获：斜杠命令/权限/问答/采样/供应商五大能力全部收敛到"共享命令层+UI 适配层"模式，
消除两端独立实现。遗留提醒：⚠️ GUI 权限弹窗 Confirm 场景默认拒绝（G1 边界）、
⚠️ /exit 的 GUI onExitRequested 未接窗口关闭、⚠️ B8 曾污染真实 settings.json 建议人工核查。

## 追加任务：会话隔离审查结论 + T6/T7（2026-08-22）

审查结论：底层机制（ChatContextManager 桶隔离 + ITranscriptService 统一入口）三端已统一，
差距在 TUI 未接线：零持久化（对话退出即丢）、无会话切换。

- [x] **T6** 会话持久化 — **架构升级：transcript 落盘下沉到引擎管道**（用户确认方向后
  从"TUI 自建 TuiSessionStore 写盘"改为三端统一方案）：
  ① 新增 `TranscriptPersistMiddleware : IChatMiddleware`（Brain）— 流结束后取
  CurrentMessageCount 快照差量增量 AppendEntries 到 {sessionId}/transcript.json，
  OnError=Continue + worker 进程守卫（对齐原 CliSession 守卫）；挂载到生产
  PipelineComposition 与测试 TestPipelineRegistration 的 Chat 管道。
  ② sessionId 统一：EngineSessionFactory 工厂生成一次 → SwitchSession 注入引擎 +
  Result.SessionId 暴露；CliSession 构造接参同源；SessionResumeStep 补
  SwitchSession（修复 resume 只改写盘目标而引擎桶仍 default 的深层不一致）。
  ③ 消除双写：删除 CliSession.AppendTranscriptEntriesAsync 手动落盘；
  TuiModeRunner 撤回手写接线，TuiSessionStore 收缩为仅 meta.json 元数据。
  ④ 落盘结构保持用户后台设计不变：sessions/{sessionId}/transcript.json 主对话 +
  subagents/{agentId}/transcript.json 树状子代理（AgentTranscriptService 原链路）。
  测试：中间件单测 5 + E2E 集成 2（MockServer 真实管道验证两轮增量无重复）+
  TuiSessionStore 2。Host.Tests **1031 全绿**、GUI 331 全绿、Brain.Context 760 全绿。
- [x] **T7** TUI 会话切换 — store 扩展 ListSessionsAsync/TryResolveTarget（纯函数：
  1-based 序号或原始 ID 直通）/SwitchToAsync（引擎桶 SwitchSession+本地 SessionId 更新）；
  TuiModeRunner 内建 `/sessions` 命令（共享 registry 前拦截）：无参=list 列出最近 20 个
  （含消息数/预览/当前标记），`/sessions <序号|ID>` → 读目标 transcript（过滤元数据条目）
  → 先切桶再灌入（对齐 SessionResumeStep 顺序）→ 清屏重绘；Tab 补全注入 "sessions"；
  工具栏 New/F1 开新会话（SessionIdGenerator 分钟偏移防同分钟冲突）。
  测试：store 5 个新增。Host.Tests **1036 全绿**、Brain.Context 760、E2E 2 全绿。

<!-- 🤖 Auto Decision: 2026-08-22 (T6 架构升级) -->
<!-- 决策: transcript 落盘从"三端各自手写"下沉为引擎管道中间件 TranscriptPersistMiddleware -->
<!-- 原因: 用户质疑自建 TuiSessionStore 重复造轮子;查证 CLI=CliSession手动/GUI=GuiSessionStore覆盖/TUI无 三套并存,违反单一实现原则 -->
<!-- 替代方案: 复用 GUI GuiSessionStore(放弃:其 Delete+Append 全量覆盖语义与 append-only 冲突);维持三套(放弃:未收敛) -->
<!-- 关键修复: SessionResumeStep 只 OverrideSessionId 不切引擎桶的深层不一致 → 补 SwitchSession -->
<!-- 验证: E2E 两轮增量无重复 + Host.Tests 1031 / GUI 331 / Brain.Context 760 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T6 sessionId 统一) -->
<!-- 决策: EngineSessionFactory 工厂生成一次 sessionId,引擎 SwitchSession + Result.SessionId 暴露,CliSession 构造接参 -->
<!-- 原因: 中间件以 contextManager.SessionId 为唯一落盘键,此前 CLI/GUI/TUI 各自管理(GUID/Generate/default)必然分裂 -->
<!-- 替代方案: 各端继续自带 ID 并在写盘时传参(放弃:中间件无法感知调用方 ID,回到三套老路) -->
<!-- 验证: 编译三端通过 + 全量回归绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-22 (T7) -->
<!-- 决策: /sessions 为 TUI 内建命令而非共享 ChatCommand;切换语义=SwitchSession+LoadSessionMessages 灌入而非纯桶切换 -->
<!-- 原因: 切换需要 UI 编排(清屏重绘/本地 history 重建)且共享 registry 无此命令;灌入链路与 /resume 完全同源,三端行为一致 -->
<!-- 替代方案: 新增共享 ChatCommand(放弃:GUI 会话列表已是独立 UI,TUI 再走命令层反而绕路);内存多桶并行(放弃:TUI 单会话模型,YAGNI) -->
<!-- 验证: store 9 测试绿 + Host.Tests 1036 / Brain.Context 760 / E2E 2 全绿 + jcctui 冒烟 ✅ -->

## T6/T7 完成总结（2026-08-22）

会话隔离审查 → 架构升级闭环：
1. **落盘责任收敛**：TranscriptPersistMiddleware 统一三端 transcript 增量写入（消除 CLI 手动/GUI 覆盖/TUI 缺失 三套并存）
2. **sessionId 同源**：EngineSessionFactory 工厂唯一生成，引擎桶+调用方一致
3. **resume 桶修复**：SessionResumeStep 补 SwitchSession（此前灌 default 桶的深层 bug）
4. **TUI 补齐**：持久化免费获得 + /sessions 切换 + New 开新会话

提交：T6=b9d791938，T7=本次。测试基线：Host.Tests 1036 / GUI 331 / Brain.Context 760 / E2E 2。
遗留提醒：⚠️ GUI GuiSessionStore.SaveActiveSession 仍全量覆盖写 transcript（与引擎增量并存可能重复），待 GUI 会话管理迁移统一入口；⚠️ 扁平 session-*.jsonl 旧文件待 MigrateLegacyAsync 清理验证。

## T8 完成：GUI 双写收敛（2026-08-22）

- [x] **T8** GUI transcript 双写收敛 — SaveViaTranscriptService 移除 Delete+Append
  消息覆盖段，只存 SessionInfo 元数据 + CustomTitle 标题；消息落盘唯一责任方=
  引擎 TranscriptPersistMiddleware。回退路径（无 ITranscriptService 的扁平 .json，
  测试隔离兼容）保持不变。
- 附带修复：重编暴露三个测试桩（StaticReplySession/UsageReportingSession/
  GatedStreamingSession/CommandRecordingSession）缺 IJccChatSession.TranscriptService
  实现——此前 GUI 测试一直跑旧 DLL 掩盖源码破损（stale DLL 教训再次验证：改接口后必须全量重编所有消费工程）。
- 测试：GuiSessionStoreTests +1（TranscriptBacked_Save 断言 Never Delete/Never Append/
  Once Meta/Once Title）。GUI **332 全绿**。

<!-- 🤖 Auto Decision: 2026-08-22 (T8) -->
<!-- 决策: GUI 统一路径 Save 只存元数据不碰消息;回退扁平 .json 路径原样保留 -->
<!-- 原因: 引擎中间件已增量写入同一文件,GUI 覆盖语义(Delete+Append)与 append-only 冲突,每轮双写+抖动 -->
<!-- 替代方案: 删除整个回退路径(放弃:测试隔离依赖它,InMemoryFileSystem 无引擎) -->
<!-- 验证: TranscriptBacked_Save 红转绿 + GUI 332 全绿 ✅ -->

## T9 完成：GUI 确认弹窗 + /exit 接线（2026-08-22）

- [x] **T9** 消化两项 G1 遗留：
  ① ExitCommand 改造 — context.Confirm 注入回调优先（UI 差异注入机制），回退 CLI
  终端 y/N；非交互且无回调时保持直接退出语义。
  ② GUI 确认链路全通 — IJccChatSession 加 SlashConfirmHandler 属性 + ExitRequested
  事件；JccChatSession 传 confirm/onExitRequested 给共享 runner；MainViewModel 转发；
  MainWindow 注入 ShowConfirmDialog（新建极简 ConfirmDialogWindow，
  Dispatcher.UIThread.InvokeAsync 后台线程同步等待，对齐 TUI painter.Invoke 模式）+
  订阅 ExitRequested 关窗。/commit、/worktree 的确认场景同时被激活。
- 附带核查：B8 settings.json 污染 — 当前 ~/.jcc/settings.json 仅含 vendor/current/
  autoFetchModels 正常键，**无残留** ✅；扁平 session-*.jsonl 迁移验证 — 6 个旧会话
  均已生成 {sessionId}/transcript.json（幂等设计保留旧文件）✅。
- 测试：ExitCommandTests +2（确认通过→Exit/拒绝→Continue）。GUI **332 全绿**。

<!-- 🤖 Auto Decision: 2026-08-22 (T9) -->
<!-- 决策: /exit 在 GUI 走真实确认弹窗而非跳过;复用 ChatCommandContext.Confirm 既有注入点 -->
<!-- 原因: 同一机制激活 /commit /worktree 等全部确认类命令,而非只修 /exit 一个点(减法思维:不新增机制,补齐既有机制的 GUI 实现) -->
<!-- 替代方案: GUI 判定非交互直接退出(放弃:绕过确认有误触风险,且 /commit 会静默放行危险操作) -->
<!-- 验证: ExitCommand 5/5 绿 + GUI 332 全绿 + settings.json 无污染 ✅ -->

## T10 完成：sessionId 五段式统一 + 消灭 default 兜底（2026-08-22）

- [x] **T10** 用户规范落地：{日期}-{项目名}-{分支}-parent-{ObjectId全局递增数}
  ① 新建 SessionIdFactory（Infrastructure.Utils）— CreateParent 五段式生成 +
  DefaultSessionId 进程级单例（Lazy 首次生成）；git 分支/项目名进程内缓存。
  ② 全库消除 sessionId 的 "default" 字面量兜底 — 脚本扫描 26 文件 27 处
  （根源 ChatContextManager 构造 + TranscriptMiddleware 写死 default 致子代理全落
  default/subagents/ 的 bug 一并修复），统一替换为 global::Core.Utils.SessionIdFactory.
  DefaultSessionId；global:: 修饰解决 Core.Agents.Coordinator.Core 命名遮蔽。
  ③ SessionIdGenerator.Generate 委托工厂，消除双实现。
  磁盘治理：新会话全部五段式；历史 default/、session-*、GUID 目录为存量数据按红线保留不删。
- 测试：SessionIdGeneratorTests 3（格式/全局递增/同分钟去重）+ TuiSessionStoreTests 断言更新。
  Host.Tests **1041 全绿**、GUI 332 全绿。

<!-- 🤖 Auto Decision: 2026-08-22 (T10) -->
<!-- 决策: 进程级 DefaultSessionId 单例替代散布的 "default" 字面量;子代理 agentId 本体不改(目录层级 subagents/+IsSidechain 已表达父子) -->
<!-- 原因: 用户明确禁止 default 兜底;单例保证无显式会话组件共享同一真实 ID,比较逻辑(ctx.SessionId != "default")语义平滑迁移 -->
<!-- 替代方案: 逐处生成新 ID(放弃:每次调用新建 git 进程+碎片化);required 强制注入(放弃:28 处消费方改造面过大) -->
<!-- 附带修复: TranscriptMiddleware 子代理 transcript 写死 default 会话 → 挂当前引擎会话 -->
<!-- 验证: Host.Tests 1041 / GUI 332 全绿;脚本先单文件验证再推广 ✅ -->
