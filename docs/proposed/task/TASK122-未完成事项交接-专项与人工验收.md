# TASK122 — 未完成事项交接（专项与人工验收）

> 状态：待接手 · 日期：2026-11-23 · 基线：`w2` @ `47f01088f`
> 用途：TASK103 §2.3 已无可按现有证据继续施工的项；本文件只交接**剩余专项/人工验收**，避免接手人重读全部取证。
> 原则：先读对应取证文档的一手证据，再决定实现；不要按 TASK075/TASK066 的旧转述直接施工。

## 一、当前已收口范围

TASK103 §2.3 的 #35–#41、#43、#44、#45a、#46、#47、#48、#49、#49a、#50、#51、#52 均已闭合，
其中包含：

- #41 递减回报检测无状态化，消除 fork 并发查询计数串扰；
- #44 TaskCreate `subject→title` 参数别名 + 提示词字段纠偏；
- #45a `MAX_STRUCTURED_OUTPUT_RETRIES` 从注释承诺落为真实解析；
- #46 `duration_api_ms` 改为 LLM API 累计耗时；
- #50 团队记忆上传前密钥扫描门禁；
- #45 `shell_background_output` 补 `block`/`timeout_ms` 与 `retrieval_status` 三态（超时不杀任务）；
- #52 Windows 默认 `RawStdinPump` 事实纠正 + 零引用 `ReadKeyPump` 归档。

本分支已随 PR #718 合入 `main`（`eeb5a2f19`），工作区干净，`w2` 与 `main` 同步。

## 二、剩余事项总表

| 编号 | 事项 | 规模 | 阻塞原因 | 建议顺序 |
|------|------|------|----------|----------|
| #52 | WT/conhost 双终端键位矩阵 | S/M | 需用户在真实终端逐项操作，自动化探针不能替代 | 1 |
| ~~#45~~ | ~~`TaskOutput` block/timeout 等待语义~~ ✅ 已闭合（[TASK123](TASK123-TaskOutput等待语义-shell后台输出阻塞与超时.md)） | — | — | — |
| ~~#50~~ | ~~团队记忆写入前 `validateInput` 密钥拦截~~ ✅ 已闭合（[TASK124](TASK124-团队记忆写入前密钥门禁-从断言到真拦截.md)） | — | — | — |
| ~~#46~~ | ~~`permission_denials` 结构化收集~~ ✅ 已闭合（[TASK125](TASK125-permission_denials结构化收集.md)） | — | — | — |
| #42 | 查询期自动记忆召回（sideQuery） | L | 新行为：每回合 LLM + 附件通道 + 会话曝光状态 | 5 |
| #40 | dream 回合结束自动触发 | L | 新行为：后台 LLM 整合，改变延迟与 token 成本 | 6 |
| #51 | Ctrl+B 主会话后台化 | L | 新行为：独立 transcript、前后台切换、取消归属 | 7 |

## 三、逐项交接

### #52 WT/conhost 双终端键位矩阵（人工）

**现状**：Windows 宿主默认 `RawStdinPump`（`native_tui/apps/NativeTui.Host/Program.cs:317`）；
`ReadKeyPump` 已归档；`ConsoleKeyAdapter` 仍被 `Win32InputPump`、`Win32ConsoleInputPump`、`ReplShell` 三处消费，
**不要再按 DSG042 原文归档 ConsoleKeyAdapter**。

**需要做**：在 Windows Terminal 与 conhost 各跑一遍交互 REPL，逐项确认字符/中文、方向键、Ctrl 组合、
Shift+Tab、Home/End/PgUp/PgDn/F1-F12、bracketed paste、鼠标点击/滚轮、resize 后输入不丢键。
建议把结果回填到 `TASK066` B6 与 `TASK121` §6，而不是只写在 PR 评论。

**不要做**：用 `jcc --selftest-input` 冒充矩阵通过。该探针只证明管道字节流的 UTF-8 解码/前缀攒齐/paste/修饰组合，
不覆盖 WT 与 conhost 的终端层差异。

**参考**：[TASK121](TASK121-DSG042-P3输入泵默认与退役复核.md)、`DSG042-tui-raw-stdin-input-pump-migration.md`。

### #45 TaskOutput block/timeout 等待语义 — ✅ 已闭合 2026-11-23（[TASK123](TASK123-TaskOutput等待语义-shell后台输出阻塞与超时.md)）

**已做**：`shell_background_output` 增 `block`（默认 true）/`timeout_ms`（默认 30000，上限 600000），
返回 `retrieval_status=success|not_ready|timeout`；超时只结束等待窗口、不取消任务；任务不存在改诊断错误。
`task_output`（todo 库）保持不动 —— 数据源不同域，不得混入后台进程语义。

**现状**：基准 `TaskOutput` 读后台 shell 任务，`block` 默认 true、`timeout` 30s；
本仓 `task_output` 读 todo 任务库，真正的后台 shell 输出是 `shell_background_output`；
底层等待原语 `ISystemActuatorRegistry.WaitForTaskAsync` 已存在，但两个输出工具都未暴露参数。

**先回答四问**（见 [TASK112](TASK112-Glob-Bash超时-TaskOutput语义-差异取证.md) §3）：

1. 挂在 `shell_background_output`，还是也改 `task_output`？建议只改前者，后者的数据源是 todo，不应混域。
2. 默认 `block` 是 true（对齐基准，改变现有调用时机）还是 false（兼容现状）？
3. 超时返回 `not_ready`/`timeout` + 已产出部分输出，还是直接报错？
4. 取消/超时如何与 actuator 的取消令牌和 TUI 停止链路对齐？

**落点**：`modules/cognition/hands_shell/shell/handlers/tool_handlers/ShellToolHandlers.cs:192-211`、
`modules/cognition/hands_shell/system_actuator/abstractions/SystemActuatorRegistry.cs:206-215`、
工具 schema/CLI 节点帮助与 TDD。

### #50 团队记忆写入前 `validateInput` 密钥拦截 — ✅ 已闭合 2026-11-23（[TASK124](TASK124-团队记忆写入前密钥门禁-从断言到真拦截.md)）

**已做**：写入链本来就有 `CheckTeamMemSecrets`，坏在 `TeamMemSecretGuard` 构造参数恒为 `null`（无人注入），
`IsTeamMemPath` 永远 false → 门禁常年敞开。改为按 `TeamMemoryLayout` 解析 `<memdir>/team-memories` 根，
并顺带统一 `TeamMemoryPaths` 的默认布局（原先两套默认目录漂移）。真机三态已验证，错误信息只回 ruleId。

**现状**：上传前门禁已补（`SyncFileTransfer.PushToRemoteAsync` 命中密钥则跳过本文件、不写索引、不发 Synced），
但工具写入前仍可把密钥写进团队记忆，属于“先落盘、出站再拦”的次优路径。

**需要做**：在团队记忆写入/编辑工具的 `validateInput` 阶段复用 `SecurityPatterns.ScanForSecretMatches`；
命中时返回 Rust 风格参数错误（带 ruleId，不带密钥文本），并补“写入被拒/普通内容可写”两项测试。
注意不要阻断其他工具的正常写入，只在团队记忆路径生效。

**参考**：[TASK119](TASK119-团队记忆密钥扫描-取证与上传门禁.md) §2/§4。

### #46 `permission_denials` 结构化收集 — ✅ 已闭合 2026-11-23（[TASK125](TASK125-permission_denials结构化收集.md)）

**已做**：`ToolResult.ErrorCode` → `ChatStreamEvent.ToolErrorCode` → `IEventConsumer.OnToolEnd(..., errorCode)` 全链贯通，
CLI 按 `ToolErrorCodes.IsPermissionDenial` 判定（收录全部 `DeniedBy*`，`PR002` 待确认不算拒绝）；
仅当无码时才保留一行文本兜底。事件层用 PD 码字符串而非枚举，避免 primitives 层反向依赖 models。

**现状**：`SdkMessage` 字段存在，`CliEventConsumer` 仍用 `resultText.Contains("拒绝")` 文本启发式；
英文/其他措辞会漏，OS 权限错误也可能被误报成用户拒绝。

**需要做**：把权限决策从引擎事件契约一路带到 `CliEventConsumer`，按结构化事件收集 denial；
涉及引擎事件 DTO、消费者、各宿主适配层，属跨 slnx 契约变更，按项目纪律跑全量测试。
不要再往文本匹配上叠关键字。

**参考**：[TASK114](TASK114-Complete协议字段复核与api时长修正.md) §0/§2。

### #42 查询期自动记忆召回

**现状**：本仓 `MemoryRelevanceSelector` 服务 `/memory search` 与 `memory_search`，是显式检索；
基准 `findRelevantMemories` 是每回合 sideQuery 自动附件召回，两者不同语义层。

**先回答四问**（见 [TASK109](TASK109-召回算法差异-判据取证.md) §3）：

1. 是否用 sideQuery 旁路调用，确保不污染会话历史？
2. 触发门：最小输入长度、每会话曝光文件/字节上限、已读文件过滤如何映射本仓附件模型？
3. 模型档位、超时、失败降级（必须不阻塞回合）；
4. 结构化 `MemoryEntry` 如何向选择器呈现“文件名+描述+mtime”清单？

**不要做**：把显式 `/memory search` 的关键词打分直接换成 sideQuery——那只会给低频命令加 LLM 依赖，
不会产生基准的自动召回。

### #40 dream 回合结束自动触发

**现状**：本仓有独立 dream pipeline exe、`/dream` 命令、`ConsolidationLock`、`AutoDreamConfig`，
但没有“回合 Stop 事件 → dream pipeline”的接线。

**先回答三问**（见 [TASK107](TASK107-dream自动触发路径-差异取证.md) §3）：

1. 自动触发是否用 sideQuery，不污染会话历史？
2. 触发失败如何降级，确保不阻塞回合、不吞用户消息？
3. 默认开关与频率门（基准默认开 + 24h/5 session，本仓配置已同默认值）是否照搬？

三问答完再立 ADR + TDD；这是新增后台 LLM 行为，不是补断链。

### #51 Ctrl+B 主会话后台化

**现状**：后台子 agent 的执行/通知/面板/停止已等价实现；
真正缺的是把“当前主会话查询自身”后台化：独立 transcript、清空前台、完成通知、前后台切换、`/clear` 后存活。
本仓全仓无 `main-session`，`ForegroundTaskRegistry.BackgroundAll` 只处理 shell 且零调用。

**先回答四问**（见 [TASK120](TASK120-LocalAgentTask后台化任务类型-差异取证.md) §4）：

1. 入口与主体：Ctrl+B 在 native TUI/CLI/GUI 哪一层？是否只允许忙时触发？前台 agent 是否共用？
2. 会话存储隔离：后台查询写独立 transcript 还是复用当前？`/clear` 重链保留哪些 ID？
3. 前后台切换：消息增量如何同步、取消令牌归谁、终态通知在“正在观看”时是否抑制？
4. 协议与开关：`task_type='local_agent'` + `agentType='main-session'` 是否进 SDK 事件/NDJSON？默认开关？

**不要做**：为对齐基准重建 `AppState.tasks` 全局多态任务表；本仓已有域服务/事件泵，重建会制造第三套状态。

## 四、接手验收基线

- 编译只编改动项目；跨域/契约变更按项目纪律跑 `Build-AllDomains.ps1 -Changed` 与必要全量测试。
- `NativeTui.Tests` 已知 `RawStdinPumpTests` 偶发 flaky，失败时单跑该 fixture 复核，不要直接判回归。
- 真机 `jcc --selftest-tui` 若报 Hub 不可达，用 `JCC_ALLOW_DIRECT_FALLBACK=1` 重跑。
- 文档回填顺序：取证文档 → TASK103 总账 → 本交接文档。不要只改代码不改账。

## 五、一句话交接

**能自动化的已知断链已经清完；剩下的不是“再补一根线”，而是六个需要产品/契约决策的新行为，
加一个只能由用户在真实终端完成的键位矩阵。** 建议从 #52 人工矩阵开始，再按 #45 → #50 → #46 → #42 → #40 → #51 推进。
