# TASK064 — TUI 对齐 Claude Code 下一批差距（M8 规划）

> 状态：**M8-1..M8-8 全部 implemented（2026-10-22）**；提交链：P0 三连 689f830b3→423187e9d→aef776136，M8-4/5/6 并行批次 f59a6a99e，M8-7 643a400e5，M8-8 7946994c3。剩余 P3 池不排期随机会动；已登记遗留=confirm/prompt 交互模态、evictAfter 终态驱逐、停止交互（x 键）、fork 快照描述字段、方案 B 块级增量 Markdown
> 状态：proposed（2026-10-22；PR #446 合并后双调研挖掘——native_tui 现状侧 + 基准源码侧）
> 来源：用户指令"挖掘还有什么没对齐 Claude Code TUI 功能的，构建新规划"
> 双调研证据：①native_tui 逐文件实证（InProcessEventConsumer/ReplShell/ReplBridgeDriver 等）②基准源码逐项核实（claude-code-rev-main/src，文件:行号 见各项）

## 一、差距总表（双调研合并）

| # | 差距项 | 本地现状 | 基准行为（证据） | 级别 |
|---|--------|---------|----------------|------|
| 1 | **逐字流式渲染** | ❌ busy 期仅 spinner；`TokenDelta(int Length)` 协议无文本（BridgeEvent.cs:65），`OnDone` 才整条上屏（InProcessEventConsumer.cs:96-110） | `StreamingMarkdown` 块级增量——按顶层块边界切稳定前缀 memoize，增长块逐 delta 重 parse（Markdown.tsx:176-235）；`⎿` 前缀流式行（Messages.tsx:703-709） | **P0** |
| 2 | **Esc 打断闭环** | ⚠️ 双击 500ms 打断已有（ReplShell.cs:941-965）；打断后已流文本**丢失**、prompt 不恢复 | 打断时 streamingText 定格为 partial 消息（REPL.tsx:2153-2161）；无有效响应时自动 rewind + 恢复输入文本 + 撤销历史条目（REPL.tsx:3028-3054） | **P0** |
| 3 | **ctrl+o 全局 transcript 模式** | ❌ ctrl+o 仅单条 thinking 展开（ReplShell.cs:500-501）；全局模态只有 Shift+↑ MessageCursor | ctrl+o 切 prompt↔transcript 全屏 verbose（useGlobalKeybindings.tsx:95-132）；less 键位 j/k/g/G/ctrl+u/d + `/` 增量搜索 + n/N 跳转 + cur/count 徽章（REPL.tsx:4470-4523）；footer 专用提示行 | **P0** |
| 4 | **工具结果折叠交互** | ⚠️ Bash/Write 折叠提示文案已有；Ctrl+O 展开断裂（只路由 thinking，MessageItems.cs:118-119 注释自认"后续项"）；**Read N files 聚合摘要完全缺失**（ToolCallItem 逐条独立） | 三套独立折叠策略：Bash 3 行（terminal.ts:7 + 差1行特例直显4行）/Write 10 行（FileWriteTool/UI.tsx:26）/Edit 全量；`CollapsedReadSearchContent` 聚合摘要行 `Read N files, Searched N patterns` + 最新操作提示行 + 700ms 防闪烁（:296-461） | P1 |
| 5 | **命令面交互断裂** | ⚠️ 执行链通但回调全 null：`/clear` 不清滚动区（clearScreen 空引用）、`/exit` 不退出（ShouldContinue 丢弃）、confirm/prompt 降级（EngineSession.cs:140-155） | CLI 侧完整交互闭环 | P1 |
| 6 | **消息操作复制** | ❌ MessageCursor 导航已有，无 c/p 复制 | `c` 复制全文（含 grouped 拼接/system-reminder 剥离 messageActions.tsx:409-441）、`p` 复制工具主参数（path/command/pattern/url 映射 :70-119）、底部操作条 `enter expand · c copy · p copy path`（:296-398） | P1 |
| 7 | **上下文告警** | ❌ 无 token 余量提示 | `N% until auto-compact` / `Context low (N%) · Run /compact`（TokenWarning.tsx:166-177）；阈值 13k/20k 缓冲（autoCompact.ts:62-65） | P1 |
| 8 | **后台任务面板** | ❌ 无 CoordinatorTaskPanel/BackgroundTasksDialog/↓ manage（footer `← agents` 仅文字无入口） | 输入框 footer 下方常驻面板（1s tick + evictAfter 驱逐 + x 停止 + Enter 查看，CoordinatorAgentStatus.tsx:34-76）+ Shift+↓ 开任务对话框（分组排序 + x 停止/f 前台化，BackgroundTasksDialog.tsx:217-289） | P1 |
| 9 | 小终端塌缩 | ❌ 仅 spinner/banner 局部门控 | `rows < inProgress*9+7` 时子代理进度塌缩单行 `In progress… · N tool uses (ctrl+o to expand)`（AgentTool/UI.tsx:469-503） | P2 |
| 10 | ctrl+s stash / ctrl+g 外部编辑器 / vim 模式 | ❌ | chat:stash 暂存自动恢复（defaultBindings.ts:85）、$EDITOR 编辑（:83-84）、VimTextInput + `-- INSERT --` | P2 |
| 11 | 大段粘贴折叠 / `!` bash 直通 / `&` 后台语法 / 图片粘贴 | ❌ | `[Pasted text #N]` 折叠引用（PromptInput.tsx:1222-1239）、`!cmd` 直通（:1206-1213）、`&` 前缀后台 | P2 |
| 12 | 未读分隔线 / 通知队列 / footer Notification 联动 | ❌ Notification 静态占位（ReplShell.cs:197） | `── N new messages ──`（Messages.tsx:631-634）、优先级+timeout 通知队列（notifications.tsx）、effort 变更刷新（PromptInput.tsx:1967-1979） | P2 |

**已确认不缺**（调研实证，勿重做）：输入历史 ↑↓+Ctrl+R ✅、排队消息面板 ✅、footer 三段（汉化）✅、斜杠补全面板/参数补全/ghost ✅、thinking 行+展开 ✅、spinner 动词/闪烁/shimmer ✅、子代理分组+活动日志 ✅、消息导航 Shift+↑ ✅、Markdown/高亮/diff 渲染 ✅、resize ✅。

## 二、M8 分批规划

> 排序原则：感知差距大的先行（流式=最大体感）→ 打断闭环依赖流式 buffer → transcript 模式独立可并行 → 折叠/命令面/复制为交互闭环 → 引擎依赖项殿后。

### M8-1 逐字流式渲染（P0，依赖：无；引擎侧 TASK043 已修好透传，事件流通）

**协议扩展**：`TokenDelta(int Length)` → 增量字段 `Text`（string?，WhenWritingNull 兼容旧 golden）；双投影：
- 进程内路径：`InProcessEventConsumer.OnText` 直发带文本事件（保留 Length 语义供 spinner 计数）
- 进程协议路径：`BridgeEventConsumer` 投影 + 引擎侧 Bridge DTO 同步

**渲染层两方案**（实施时二选一）：
- 方案 A（先简）：流式期纯文本逐字追加（`StreamingTextItem` 原地 append，⎿ 前缀），`OnDone` 替换为完整 Markdown 渲染
- 方案 B（对齐基准）：块级增量 Markdown——顶层块边界切稳定前缀（测量结果缓存）+ 增长块逐 delta 重投影（复用 Markdig 卫星）
- 判据：A 先行保首字延迟体感（开心路径），B 为后续升级项——A 的 StreamingTextItem 接口按 B 的增量契约设计，替换不破消费方

**验收**：busy 期滚动区正文逐字增长（E2E/selftest-turn 实证首 token 到达后正文行长度递增）；打断时已流文本不丢（衔接 M8-2）；像素回归 0 差异；JSON 契约旧用例零破坏。

**✅ M8-1 实施收口（2026-10-22，689f830b3）**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| TokenDelta 协议 +Text 字段（WhenWritingNull 旧形态零破坏）双镜像 | InProcessEventConsumer/BridgeEventConsumer OnText | ✅ | ✅ 单元：协议 JSON 新旧双形态 + Consumer 文本透传 |
| StreamingTextItem 可变缓冲逐帧增长 | Driver 惰性建项/原地追加/AssistantMessage 原位替换 | ✅ | ✅ 渲染 6 测试（前缀/续行 6 显示列对齐/多行）+ Driver 8 测试（替换/EndTurn 兜底定格 partial/BeginTurn 防残留） |
| SimulatedEngine 预分片演示 | Demo 流式可观测 | ✅ | ✅ 虚拟时钟确定性（进入 Responding 即带文本增量） |
| 全链回归 | — | ✅ | ✅ Core 841 + Host.Tests 64 + host.tests 1382 + 子代理 E2E 3/3 + selftest 真跑 EXIT=0 |

### M8-2 Esc 打断闭环（P0，依赖：M8-1——partial 定格需流式 buffer）

- 打断时流式 buffer 定格为 partial AssistantMessage（基准 `[user, partial, interrupted]` 顺序）
- 无有效响应时（打断守卫条件对齐 REPL.tsx:3028-3054）自动恢复用户输入到输入框 + 撤销该条历史记录
- Esc 重载链补全：排队命令弹回输入框（基准 popAllCommandsFromQueue 语义——本地已有队列，补弹回）

**验收**：打断后 partial 文本可见且标记 interrupted；恢复路径输入框文本回填；单测锁守卫条件（有响应/无响应两分支）。

**✅ M8-2 实施收口（2026-10-22，423187e9d）**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| partial 定格（EndTurn 兜底） | Driver EndTurn 非空流式项 → AssistantMessage | ✅ | ✅ M8-1 测试集（EndTurn_WithLeftoverStreamingText） |
| 无响应自动恢复 | DrainEngine 截获 TurnInterrupted → RestoreInterruptedInput | ✅ | ✅ 三分支测试：无响应恢复（输入回填+用户消息移除+历史 RemoveLast）/partial 不恢复/完整不恢复 |
| 队列弹回 | HandleEscape 忙时优先于双击打断 | ✅ | ✅ 单击弹回按行拼接+清队列+不 Interrupt；无队列双击打断回归守卫 |

### M8-3 ctrl+o 全局 transcript 模式（P0，依赖：无——与 M8-1 可并行）

- ReplShell 第六交互态（分派链：permission > modelPicker > search > cursor > **transcript** > suggestion）
- verbose 渲染开关：全部消息走展开形态（thinking 全文/工具全量/diff 不折叠）——消费既有 ToggleThinking/展开基建
- less 键位：j/k/g/G/ctrl+u/ctrl+d/home/end + `/` 增量搜索栏（复用 HistorySearch 的搜索基建模式）+ n/N 跳转 + cur/count
- 专用 footer：`详细转录 · ctrl+o 切换 · ↑↓ 滚动 · ctrl+e 全部展开/收起`

**验收**：ctrl+o 双向切换；搜索高亮+跳转；verbose 下 thinking/diff/工具全展开（与折叠提示行联动消隐）。

**✅ M8-3 实施收口（2026-10-22，aef776136）**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 第六交互态（分派链 search 后 cursor 前） | HandleTranscriptKey 全键模态 | ✅ | ✅ 吞键守卫（字符不进输入框） |
| verbose 全展开/收起 | Enter/Exit + footer Notification 存还原 | ✅ | ✅ 进=全部 CoT 展开+提示；出=收起+还原；无 CoT 仍可切 |
| less 键位 | j/k/↑↓/g/G/Home/End/ctrl+u/d/PageUp/Down | ✅ | ✅ 视口滚动路径复用（inline 原生回看语义保持） |
| / 搜索 | ITextMatchable 七消息项 + Enter 跳匹配（环形）+ Esc 清 | ✅ | ✅ 查询累积/footer 显示/跳转 ScrollTop 断言/Esc 清除/退模态 |
| 语义演进回归 | ctrl+o 单条→全局（旧测试同步全局断言） | ✅ | ✅ Core 853 全绿（含 StructuredMessageTests 更新） |

**✅ M8-4/5/6 并行子代理批次实施收口（2026-10-22，f59a6a99e）**：三子代理文件面互斥分工 + 主智能体收编 ReplShell 落地件。

| 批次 | 基建实现 | 已实现 | 已验收 |
|------|---------|--------|--------|
| M8-4 | CollapsedToolItem 替换式聚合（连续 Read/Grep/Glob 第二次起原位折叠 + 最新操作⎿提示行）+ DiffResultItem ToggleExpand + transcript verbose 态 Diff 全展开 | ✅ | ✅ 20 新测试（聚合 9 + 折叠 4 + 渲染 7）|
| M8-5 | TuiCommandCallbacks 延迟绑定容器 + clearScreen/onExitRequested 全链 + ReplShell 清屏/退出公开入口 + RenderAndFlush 2J+3J 消费块 + Csi.EraseSavedLines | ✅ | ✅ 6 新测试（回调触发/反馈上屏/降级契约）；confirm/prompt 登记遗留（TUI 命令确认模态独立任务）|
| M8-6 | 导航态 c 复制全文/p 复制主参数 + MessageActionsBar 底部操作条（滚动区尾部常驻项模式）| ✅ | ✅ 16 新测试 |
| 批次回归 | — | ✅ | ✅ Core 889 + Host.Tests 74 + host.tests 1382 + selftest 真跑 EXIT=0 |

### M8-4 工具折叠交互统一 + Read N files 聚合（P1，依赖：M8-3——展开消费 transcript/局部展开双通道）

- Ctrl+O 展开路由统一：thinking/diff/Bash/Write 折叠共享统一展开入口（消息级 ToggleExpand，不再只路由最后一条 thinking）
- Bash 3 行差 1 行特例（基准 terminal.ts:44-54）；Write 10 行上限语义已有
- `Read N files` 聚合摘要行：连续同类工具调用（Read/Grep/Glob）合并单行摘要 + 最新操作提示行（⎿ path/pattern）+ 700ms 防闪烁——落点 ReplBridgeDriver 的 ToolCall 路由（连续同类合并）+ 新 CollapsedToolItem

**验收**：折叠提示行点击/按键展开可用；连续 3 次 Read 显示 `Read 3 files` + 最新文件提示行；verbose 全展开。

### M8-5 命令面交互闭环（P1，依赖：无）

- `clearScreen` 回调接 TUI 清滚动区（ReplShell 暴露清屏入口）
- `onExitRequested` 接线 → `/exit` 后 TUI 退出（走 ShouldContinue 主循环出口）
- confirm/prompt 交互回调接 TUI 模态（复用 PermissionCoordinator TCS 桥模式）；readPassword 输入遮蔽

**验收**：/clear 滚动区清空+banner 重现；/exit 进程退出码 0；交互命令不降级（ExitCommand 走确认分支）。

### M8-6 消息操作复制（P1，依赖：无——MessageCursor 基建已有）

- 消息导航态补 `c`（复制全文——grouped 拼接+system-reminder 剥离）与 `p`（复制工具主参数：path/command/pattern/url 按 ToolName 映射）
- 底部操作条 `enter 展开 · c 复制 · p 复制参数 · ↑↓ 导航 · esc 返回`
- Windows 剪贴板路径（现有引擎剪贴板服务复用）

**验收**：导航态 c/p 写入剪贴板可粘贴验证；操作条渲染像素对齐（上边框+间隔符）。

**✅ M8-7 实施收口（2026-10-22，643a400e5）**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 引擎侧：IEventConsumer.OnContextUsage DIM + ContextUsage 协议事件 + BridgeEventConsumer 显式实现 | SessionController.ProcessEvent onDone 发射（ChatUsageProcessor 在中间件链外且 Singleton 作用域错配弃） | ✅ | ✅ host.tests 41（发射 3 + 映射 + JSON 契约） |
| TUI 侧：ContextWarning 独立槽位 + ResolveContextWarning 三色（≤10 error/≤25 warning/>25 清空） | FooterBar 行 2 告警优先渲染 | ✅ | ✅ 13 新测试（路由 8 + 渲染 4 + 映射 1） |

**✅ M8-8 实施收口（2026-10-22，7946994c3）**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 引擎侧：BackgroundTaskEventPump（订阅 ForkCompleted/TeammateCompleted 绕开回合作用域通道 + 回合终态快照） | BridgeServeLoop 可选参接线 + EngineTurnClient Consumer 出口（主收编） | ✅ | ✅ 泵 12 + consumer/契约 |
| TUI 侧：BackgroundTaskPanel 常驻面板（upsert/终态驻留/空面板零渲染/上限10窗口/三态色） | 排队面板上方渲染 | ✅ | ✅ 面板 15 + 路由 4 + 映射 1 |
| 批次回归 | — | ✅ | ✅ Core 919 + Host.Tests 76 + host.tests 1401 + selftest 真跑 EXIT=0 |

### M8-7 上下文告警（P1，依赖：引擎侧 token 余量事件——需先调研引擎 contextWindow 消费链）

- footer Notification 槽位真数据化：`N% 直到自动压缩`（dim） / `上下文不足 (N%) · 运行 /compact`（warning/error 色）
- 引擎侧需求：每回合 Done 时上报 contextWindow 用量百分比（IEventConsumer 扩展或 session_info 增量）——落 DSG 调研后定

**验收**：长会话实测百分比递增显示；/compact 后百分比回落。

### M8-8 后台任务面板（P1，依赖：引擎侧后台任务事件源——teammate/fork 后台代理状态查询）

- CoordinatorTaskPanel 式输入框 footer 下方常驻面板（Enter 查看 / x 停止 / evictAfter 驱逐 / 1s tick）
- Shift+↓ 开 BackgroundTasksDialog（分组列表 + x 停止/f 前台化/Enter 详情）
- footer `← agents` 提示接真实入口（当前 LeftArrow 是光标移动——基准也非 ← 键，实为文字提示，改为 ↓ manage 对齐）

**验收**：agents 后台 spawn 后面板出现该任务；x 停止后代理真实终止；Enter 详情切换。

### P3 池（不排期，随机会动）

小终端塌缩（#9）/ ctrl+s stash / ctrl+g 外部编辑器 / vim / 大段粘贴折叠 / `!` `&` 直通 / 未读分隔线 / 通知队列系统 / effort 变更 Notification 联动 / OSC 9;4 终端进度上报 / 键位可配置系统。

## 三、执行说明

- 每批走 TDD 红绿 + 像素回归门禁 + selftest 冒烟 + TASK 文档验收表四列收口
- M8-1/M8-3 可双线并行（协议扩展 vs 纯 TUI 交互态，无文件冲突面——ReplShell 分派链追加注意并行会话共享规则）
- M8-7/M8-8 各含引擎侧前置调研，先出 mini-DSG 再实施
- 全批次汉化对齐现状（功能性文案中文，基准动词/符号资产保留）

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: M8 八批排序 = 感知差距优先（流式 P0 三连）→ 交互闭环 P1 五批 → 引擎依赖殿后 -->
<!-- 原因: 逐字流式是最大体感差距且引擎侧 TASK043 已扫清透传障碍（只剩 bridge 协议+渲染层）；M8-2 依赖 M8-1 的流式 buffer；M8-3 独立可并行提高吞吐；M8-7/M8-8 有引擎前置不确定项故殿后 -->
<!-- 替代方案: 按基准源码模块序全量复刻（无优先级区分，工期长且先做低感知项）；按实现难度序（折叠交互最易但价值低于流式）——均弃 -->
<!-- 验证: 双调研证据链已列（本地文件行号+基准文件行号）✅ -->
