# TASK070 — 跨域对齐 ClaudeCode 剩余功能（DSG046 三批实施）

> 状态：proposed（计划先行，用户目标"对齐 ClaudeCode 的剩余功能 + 自有验收方式 + 先构建计划"）
> 来源：TASK069 批次 2/3 跨域登记项（用户已决策"全部做"）→ DSG046 设计文档
> 范围：DSG046 三批（压缩闭环 / 重试感知 / 权限持久化）+ B-6 --continue 跨项目过滤
> 锚点实证：2026-10-24 全部锚点代码已逐一核对（见各批锚点表），DSG046 假设与现状一致
> 基准：`D:\project\claude-code-rust\claude-code-rev-main\src\`（autoCompact.ts / withRetry.ts / permissionRuleParser.ts / compact.ts）

## 一、剩余功能清单（对齐范围界定）

| 项 | 内容 | 状态 | 归属 |
|----|------|------|------|
| C6 | 压缩完成事件（compact 双侧协议缺失） | 未实施（CompactCompleted 全仓零命中） | 本任务批次 1 |
| C4 | 自动压缩接线（AutoCompactService.ShouldAutoCompact 无生产调用方） | 未实施 | 本任务批次 1 |
| C6' | TUI 压缩警告抑制（TokenUsageTracker.Reset 注释承诺无调用点） | 未实施 | 本任务批次 1 |
| B-6 | --continue 跨项目恢复（SessionResumeStep 无 ProjectPath 过滤） | 未实施 | 本任务批次 1 |
| E2/E3 | 重试感知上屏（QueryEngine.cs:172-185 静默重试无 UI 感知） | 未实施 | 本任务批次 2 |
| P1 | always-allow 持久化接线（TUI/CLI 确认处理器仅内存 30min；PermissionChecker.AddToAutoApprovedAndPersistAsync 持久化通道已存在未接线） | 未实施 | 本任务批次 3 |
| P2 | 规则内容粒度匹配（"Bash(npm run:*)" 前缀规则被 ToolListPermissionMiddleware 退化为工具级全放行；ParsePermissionRuleValue 私有散落） | 未实施 | 本任务批次 3 |
| TASK069 域内差异项（S1-S6/M1-M6/C1-C3/C5/E1/E4/P3-P7/三·补 等） | TUI 域内改造 | 待用户一问一答决策（AGENTS.md 基准对比工作流第 1 条，禁止 AI 自行对齐） | 后续任务 |
| P2 三档对话框 UI（工具级/前缀级/会话级选择 + 可编辑前缀） | TUI 对话框 UX 扩展 | 本任务先落"前缀规则自动生成"最小闭环，对话框三档 UI 待用户决策 | 后续任务 |

## 二、批次划分（按 DSG046 第二节，锚点实证后细化）

### 批次 1 — 压缩闭环（C6 + C4 + C6' + B-6）

| 子项 | 改造锚点（实证） | 内容 |
|------|-----------------|------|
| C6 协议 | `modules/apps/engine/bridge/BridgeProtocol.cs` + `native_tui/src/NativeTui.Core/bridge/BridgeEvent.cs` | 双侧新增 `compact_completed` 事件（MessagesSummarized/OriginalTokens/CompressedTokens/IsAuto），[JsonDerivedType] 同步注册，JSON 形状由测试锁定 |
| C6 手动发射 | `modules/abstractions/.../IChatCommand.cs`（ChatCommandResult 加 Data 载荷）+ `modules/composition/slash/brain/session/CompactCommand.cs`（成功路径挂 CompactSummaryData）+ `native_tui/apps/NativeTui.Host/EngineSession.cs:DispatchSlashCommandAsync`（result.Data 投影 consumer.OnCompactCompleted） | /compact 成功 → 事件上屏 |
| C4 自动压缩 | `modules/apps/engine/adapters/SessionController.cs`（OnDone 捕获 usage → PostProcessMainAgentAsync 后 ShouldAutoCompact 判定 → 异步压缩）+ `modules/cognition/prompts/templates/memory/CompactPromptTemplate.cs`（摘要 prompt 复用）+ `ChatService.CompactHistoryAsync`（历史压实落点，与 CompactCommand 同链） | 回合末异步触发，IsAuto=true 发同一事件；autoCompactEnabled=false / 阈值未达 / usage 缺失 三重门控 |
| C6' 抑制 | `native_tui/src/NativeTui.Core/bridge/ReplBridgeDriver.cs`（CompactCompleted → TokenUsageTracker.Reset + 通知） | 压缩后 footer 警告归零重算 |
| B-6 | `modules/apps/cli/entry/startup/core/SessionResumeStep.cs:FindMostRecentSessionIdAsync` | 按 meta.json SessionInfo.ProjectPath == 当前目录过滤（逐候选拿 GetSessionInfoAsync），无匹配 → 提示无会话 |

### 批次 2 — 重试感知（E2/E3）

| 子项 | 改造锚点（实证） | 内容 |
|------|-----------------|------|
| 回调链 | `modules/abstractions/.../QueryOptions.cs`（新增 OnRetry 回调，WriteToTranscript 先例）+ `modules/cognition/brain/query/query2/token_budget/core/QueryEngine.cs:172-185`（catch 处 invoke）+ `modules/llm/agents/Coordinator/Fork/AgentBase.cs:BuildChatOptions`（RetryNotifier 透传）+ `modules/apps/engine/adapters/SessionController.cs`（构造期绑定 consumer.OnRetry） | 主代理生产路径（AgentBase→IQueryEngine.QueryAsync 实证确认）重试可见 |
| 消费接口 | `modules/abstractions/.../IEventConsumer.cs`（OnRetry DIM，OnContextUsage 先例）+ `modules/apps/engine/adapters/BridgeEventConsumer.cs` + `native_tui/apps/NativeTui.Host/InProcessEventConsumer.cs` | → BridgeEvent.Retry JSON 行 |
| TUI 提示 | `native_tui/src/NativeTui.Core/bridge/BridgeEvent.cs` + `ReplBridgeDriver.cs`（Retry → spinner.Message "Retrying in Xs (N/M)"，非 ErrorMessageItem） | spinner 行倒计时式提示 |

### 批次 3 — 权限规则持久化（P1/P2）

| 子项 | 改造锚点（实证） | 内容 |
|------|-----------------|------|
| 解析器 | 新 `modules/abstractions/abstractions/abs_guard/security/permission/PermissionRuleParser.cs`（"Tool"/"Tool(content)" Parse/Format 双向 + 冒号前缀 "cmd:*" 语义 + 转义）；`ServiceRegistration.ParsePermissionRuleValue` 与 `PermissionChecker.AddToAutoApprovedAndPersistAsync` 手拼处改委托（唯一数据源） | Parse/Format 往返 + 前缀匹配纯函数 |
| 判定链 | `modules/guard/guard/permission/permission2/tool_handlers/classification/ToolListPermissionMiddleware.cs`（工具级规则才整工具放行；内容级规则参数匹配——前缀 `:*` / 精确；deny 优先于 allow） | 内容粒度判定（实证：现 Contains(toolName) 把 "Bash(npm run:*)" 退化成全量放行 Bash） |
| P1 接线 | `native_tui/apps/NativeTui.Host/TuiPermissionConfirmationHandler.cs` AlwaysAllow 分支 + CLI 先例 `CliPermissionConfirmationHandler`：工具级规则 → `PermissionChecker.AddToAutoApprovedAndPersistAsync`（持久化通道已存在）；Bash 类带参 → 前缀规则 "Bash(首词:*)"（对齐 TS 前缀粒度） | AlwaysAllow → settings.json 永久 + 内存即时生效（AddToAutoApproved 双写先例） |
| 热加载 | 判定链读规则处按 settings.json mtime 失效重读（启动加载已存在 LoadPermissionsFromSettingsAsync；运行期外部编辑也生效） | mtime 快照 + 重读 |

## 三、自有验收方式（每批强制，缺一不提交）

1. **红测试先行**：每子项先写失败测试（协议镜像/路由/行为三分面），红→实现→绿。
2. **协议镜像锁定**：compact_completed / retry 两侧 [JsonDerivedType] 注册 + JSON golden（BridgeProtocolTests / BridgeJsonContractTests 先例）——防止双侧漂移。
3. **全量回归**（主智能体亲自跑，禁止 subAgent 代跑）：受影响域测试项目全绿——批次 1：composition+cognition+NativeTui.Tests+NativeTui.Host.Tests；批次 2：cognition+llm+apps+NativeTui 全家；批次 3：guard+guard.security+NativeTui.Host。
4. **真实 exe 验收**（ADR 0080，非 mock）：Host `--selftest` EXIT=0；批次 1 另跑 jcc bridge_serve 冒烟验证 compact_completed 事件行真实输出。
5. **四列验收表**：本文件末尾，每批完成后回填（实现 commit + 测试证据 + 验收记录）。
6. **提交纪律**：Debug 增量编译零警告 → 测试绿 → `git commit`（功能分支 w1，禁 push main）→ 下一批。

## 四、设计决策记录

- **D1 C4 用 ShouldAutoCompact + ChatService 链而非 ICompactService.CompactAsync 管道**：CompactCommand 手动路径实证走 ChatService.SendMessageAsync(CompactPromptTemplate)→CompactHistoryAsync；auto 路径与其同链保证行为一致（摘要格式/历史压实语义同源），AutoCompactService 承担判定门控（autoCompactEnabled/连续失败熔断齐备）。
- **D2 E2 走 QueryOptions.OnRetry 回调链而非 AsyncLocal 通道**：QueryOptions.WriteToTranscript 回调先例同形态；AgentBase.BuildChatOptions 已是 options 组装唯一点（实证 :614-640），三条透传链（cognition→abstractions→llm→apps）比新增 Ambient 通道少一套作用域管理；重试本体不新增（DSG046 修正记录：防双层重试放大）。
- **D3 P2 判定链先落最小闭环**：TUI 对话框三档（工具级/前缀级/会话级 + 可编辑前缀）UX 超出批次表，先落"AlwaysAllow 自动生成前缀规则"（Bash 首词）+ 内容粒度匹配；对话框三档 UI 登记待用户决策（一问一答纪律）。
- **D4 B-6 严格项目内过滤**：--resume 按 id 显式指定可跨项目（对齐 TS resume 显式选择语义），仅 --continue 隐式取最近时按 ProjectPath 过滤（DSG046 只登记 B-6 --continue）。

## 五、验收表（四列 — 每批完成后回填）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| C6 协议双侧 `compact_completed`（engine BridgeProtocol + TUI BridgeEvent `[JsonDerivedType]` 同步注册） | BridgeEventConsumer.OnCompactCompleted（stdout JSON）+ InProcessEventConsumer（内存事件） | ✅ | ✅（双侧金样逐字节镜像：BridgeEventConsumerTests + BridgeProtocolTests ReadEvent/Roundtrip） |
| C6 手动发射链（ChatCommandResult.Data + ContinueWith + SlashCommandResult.Data 透传 + CompactCommand 成功路径挂载） | EngineSession.DispatchSlashCommandAsync 投影 OnCompactCompleted(isAuto:false) | ✅ | ✅（EngineSessionCompactProjectionTests：真实 SlashCommandRunner+CompactCommand 驱动，事件先于 assistant_message 断言） |
| C4 自动压缩接线（SessionController._lastDoneUsage 捕获 + TryTriggerAutoCompact + RunAutoCompactAsync 后台执行） | AutoCompactService.ShouldAutoCompact 首个生产调用方；CompactPromptTemplate+CompactHistoryAsync 与手动路径同链 | ✅ | ✅（SessionControllerTests 4 用例：阈值触发/未达跳过/无服务跳过/失败不影响回合；LastAutoCompactTask 观察点消除时序） |
| C6' TUI 警告抑制（ReplBridgeDriver CompactCompleted → TokenUsageTracker.Reset + 通知槽） | ReplShell 装配的 tracker + FooterState.Notifications | ✅ | ✅（ReplBridgeDriverTests 2 用例：Reset 后 Warning 归 None + 通知文本；未注入静默容忍） |
| B-6 --continue 项目过滤（FindMostRecentSessionIdAsync 参数化 currentProject + meta.json ProjectPath 归一匹配） | SessionResumeStep.InvokeAsync（Environment.CurrentDirectory 实参） | ✅ | ✅（SessionResumeStepTests 3 用例：跨项目过滤/无匹配 null/无 meta 跳过） |
| 真实 exe 验收（ADR 0080） | NativeTui.Host --selftest | ✅ | ✅（真实 exe EXIT=0：引擎进程内组装 + session_info 握手 + 权限回执；回归 host.tests 1468 / NativeTui.Tests 2924 / composition.tests 1000 / Host.Tests 139 全绿） |

> ⚠️ bridge_serve 实进程 compact_completed 冒烟需配真实供应商（LLM 摘要调用）——事件行形状已由双侧金样锁定，宿主投影链已由真实命令驱动测试覆盖；实机验收登记待用户配好供应商后随批次 2 顺带执行。

| E2 回调链（QueryRetryNotification + QueryOptions.OnRetry + QueryEngine.NotifyRetry catch 发射） | AgentBase.RetryNotifier → BuildChatOptions（options 组装唯一点）；SessionController 构造期绑定 consumer.OnRetry | ✅ | ✅（QueryEngineTests NotifyRetry 3 用例 internal 测试面 + AgentBaseRetryNotifierTests 透传 2 用例 + SessionControllerTests 绑定 2 用例） |
| E2 消费协议（双侧 `retry` 事件 `[JsonDerivedType]` + BridgeEventConsumer/InProcessEventConsumer 直投影） | ReplBridgeDriver 路由 spinner.Message | ✅ | ✅（双侧金样：BridgeEventConsumerTests + BridgeProtocolTests ReadEvent/Roundtrip；编码器差异已登记 — TUI 侧仅测试往返） |
| E3 TUI spinner 提示（"Retrying in Xs (N/M)"，非 ErrorMessageItem） | SpinnerItem.Message（BeginTurn/EndTurn 复位不残留） | ✅ | ✅（ReplBridgeDriverTests 3 用例：1s 显示/亚秒 ceil/回合结束清空） |
| 批次2 真实 exe 验收（ADR 0080） | NativeTui.Host --selftest | ✅ | ✅（真实 exe EXIT=0；回归 brain.other 415 / agents 643 / brain.context 950 / host.tests 1471 / NativeTui.Tests 2929 / Host.Tests 140 全绿） |

| P1-P2 解析器（PermissionRuleParser Parse/Format/MatchesContent/ExtractCommand — 唯一数据源） | ServiceRegistration 启动加载 / PermissionChecker 持久化 / ToolListPermissionMiddleware 判定 / PermissionAwareToolExecutor AlwaysAllow 规则生成 全部委托 | ✅ | ✅（PermissionRuleParserTests 6 组 Theory：三形态解析/非法 null/往返一致/前缀精确匹配/命令提取） |
| P2 判定链内容粒度（前缀规则参数匹配才决策，未命中落确认） | ToolListPermissionMiddleware.MatchesRule（deny 优先于 allow） | ✅ | ✅（ToolListPermissionGranularTests 6 用例：工具级整放行/前缀命中/前缀未命中续链/无 command 续链/deny 优先/未列出续链） |
| P1 AlwaysAllow 持久化（IToolPermissionManager.ApproveToolPermanentlyAsync + Executor 接线 Shell 前缀规则） | PermissionManager → PermissionChecker.AddToAutoApprovedAndPersistAsync（内存+settings.json 双写）；TUI/CLI 两路径共用 | ✅ | ✅（Executor 3 用例：AlwaysAllow 工具级/带 command 前缀 "cmd:*"/Allow 不持久化 + PermissionManagerTest 2 用例内存生效与后缀保留） |
| 热加载（PermissionRulesFileLoader 唯一加载实现 + PermissionChecker mtime 前段检查差量同步） | 启动加载（ServiceRegistration 委托）与运行期重读同链 | ✅ | ✅（PermissionCheckerTest：settings.json 出现后判定命中 + 无文件快速路径；loader 异常沿用内存规则） |
| 批次3 真实 exe 验收（ADR 0080） | NativeTui.Host --selftest | ✅ | ✅（真实 exe EXIT=0；回归 guard.security 1133 / guard.config 1304 / guard.hooks 574 / mcp_tool_dispatch 523 / host.tests 1471 / NativeTui.Tests 2929 / Host.Tests 140 全绿） |

### CI #959 修复轮（2026-10-25 — 三类根因 + 防复发机制）

| 根因 | 修复 | 防复发机制 |
|------|------|-----------|
| ① 接口扩展遗漏 native_gui 两域 fake（CS0535，本地只编译改动域） | GuiPermissionConfirmationHandlerTests / JccChatSessionPermissionSeamTests 两 fake 补 ApproveToolPermanentlyAsync | **全仓编译脚本** `non_deliverables_tools/scripts/Build-AllDomains.ps1`（16 域 slnx + host.tests + native_gui csproj）+ AGENTS.md 基准对比工作流第 6 条纪律 |
| ② B-6 测试硬编码 Windows 盘符路径，Linux CI Path.GetFullPath 语义分叉 | 测试改 Path.Combine(GetTempPath()) 跨平台构造 | **新分析器规则 JCC1018**（语法分析器方案 — 测试项目盘符路径字面量检测，URL 协议不误报，生产项目豁免；aot_safety.tests 5 用例锁规则行为）+ 96 个存量 Windows 行为测试文件级 pragma 豁免 + AGENTS.md 第 7 条纪律 |
| ③ AsyncLockDiagnosisTests 诊断 sink 时序 flaky（SpinUntil 被 holder 自己的 WAIT-START 提前满足，waiter 未排队即释放） | 阈值 -1ms（任何排队等待必超，不依赖真实等待时长）+ WAIT-START 计数 ≥2 区分 holder/waiter | 确定性改造归档（flaky 两次 CI 根治）；注释记录根因链 |

修复轮回归：全仓编译 20 目标 EXIT=0 + async_lock 600 + SessionResumeStepTests 10 + native_gui 379/36 + aot_safety.tests 254 + host.tests 1471 全绿。

## 六、决策记录（过程追加）

<!-- 🤖 Auto Decision: 2026-10-24 批次1 -->
<!-- 决策: compact_completed 四参最小事件面 + C4 走 ShouldAutoCompact+ChatService 同链 + B-6 currentProject 参数化测试面 -->
<!-- 原因: 与手动 /compact 路径行为同源（摘要模板/历史压实）；SLASH 投影复用 Data 载荷不新增命令层事件通道 -->
<!-- 替代方案: C4 走 ICompactService.CompactAsync 管道 —— 与 ChatService 历史压实脱节，摘要落库语义分裂，弃 -->
<!-- 验证: host.tests 1468 + NativeTui.Tests 2924 + composition.tests 1000 + Host.Tests 139 全绿 + selftest EXIT=0 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-24 批次2 -->
<!-- 决策: E2 走 QueryOptions.OnRetry 回调链（AgentBase.BuildChatOptions 唯一组装点透传），NotifyRetry internal 测试面 -->
<!-- 原因: 与 WriteToTranscript 回调先例同形态；避免新增 Ambient AsyncLocal 通道的作用域管理成本 -->
<!-- 替代方案: SubAgentEventChannel 复用 —— 语义错位（子代理通道承载主回合重试），弃 -->
<!-- 验证: brain.other 415 + agents 643 + brain.context 950 + host.tests 1471 + NativeTui.Tests 2929 全绿 + selftest EXIT=0 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-24 批次3 -->
<!-- 决策: AlwaysAllow 持久化接线在 PermissionAwareToolExecutor（TUI/CLI 共用确认闭环点）而非各 handler；前缀规则 ":*" 语义对齐 TS；WebFetch domain 等非命令内容规则保持整工具语义由专用中间件兜底 -->
<!-- 原因: executor 同时持有 toolName+arguments+action，handler 层无参数通道；P2 实证缺陷（Contains(toolName) 把前缀规则退化成全量放行）在此一并消除 -->
<!-- 替代方案: TUI handler 内持久化 — 无 arguments 可提取且 CLI 需重复接线，弃 -->
<!-- 验证: guard.security 1133 + guard.config 1304 + guard.hooks 574 + mcp 523 + host.tests 1471 + NativeTui.Tests 2929 全绿 + selftest EXIT=0 ✅ -->
