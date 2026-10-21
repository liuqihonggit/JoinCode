# TASK060 — DSH 剩余对齐与验收收尾

> 延续 [TASK058](TASK058-DSH引擎深度对齐与DTO统一.md)（B1–B8 已落地并真机验收）与
> [ADR 0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)。
> 目标：把「已实现但验证手段过期」与「尚未实现的成块能力」分清，先恢复自动化验收能力，再逐项推进。

## 一、现状结论（2026-10-21 检查）

- TASK058 B1–B8 已实现且单测绿（`native_gui/tests/join_code_gui.tests` 270 通过 / 0 失败），NativeGui.Host Debug 编译通过。
- 目录已迁移到 `native_gui/src/NativeGui.Core/web_host/*`（ADR 0145），`modules/apps/gui/*` 旧路径不再存在。
- **遗留不一致**：`non_deliverables_tools/dsh_parity/dumps/*.mjs` 仍按旧存储/旧路径断言：
  - `dsh-webui-prompt.mjs` 断言 `sessions/<id>/transcript.json`（现为 `transcript.jsonl`，ADR 0144）。
  - `dsh-webui-history.mjs` 以 JSON 数组写 `transcript.json` 播种历史（现引擎优先读 `.jsonl`）。
  - `make-jcc-home-messages.mjs`、`make-fixture-session.mjs` 同样按旧格式写种子数据。
- E2E 启动方式仍可用：`NativeGui.Host.exe` 默认进 DSH 前端（`--webui` 已并入默认路径，`--webui-smoke` 是遗留自研面）。

## 二、剩余能力盘点

| # | 能力 | 现状 | 处理 |
|---|------|------|------|
| 1 | E2E 脚本与新存储/路径同步 | 断言过期 | **本批 B9**（恢复回归能力） |
| 2 | 审批弹窗真机交互验收（TASK058 B1 未竟项） | `$events` 桥已挂，未实测点击 | **B10**（桩触发权限工具 + 截图验收） |
| 3 | steer（轮内注入） | `session/steer-unavailable` 诚实拒绝 | **B11**（先 ADR/spec，再改引擎 StreamAsync） |
| 4 | `assistant-stream` 逐字流式 | `revision=0` | 依赖引擎增量事件边界，随 B11 一并评估 |
| 5 | terminal（10 方法）/ goals（7 方法+事件）/ workspaceFiles/changes | 未实现 | ⬜ 另立项目（需引擎大扩展），本任务不展开 |
| 6 | 审批**审计事件**（`approval/asked` + `approval/decided`）落会话日志 | 只发 `$events` 瞬时帧，无会话日志 | **B12**（补齐后历史回放与 DSH 一致） |

## 三、批次计划（每批 红测试 → 实现 → 编译 → 绿测试 → 文档 → commit）

| 批次 | 内容 | 状态 |
|------|------|------|
| B9 | E2E 脚本同步：`transcript.jsonl` 优先/`.json` 回退、播种改写 JSONL、exe 路径与文档对齐 | ✅ 已验收（见 §四） |
| B10 | 审批弹窗真机验收：桩返回需审批的工具调用 → 断言 `approval/request` waterfall → 前端面板 → `$events/result` 回帖 → 引擎按判定放行/拒绝 | ✅ 已验收（见 §五） |
| B12 | 审批审计事件补口：`approval/asked` + `approval/decided` 轮内成对落会话日志（含刷新回放） | ✅ 已验收（见 §六） |
| B11 | steer 引擎扩展：ADR 记录轮内注入点（Actor 邮箱/邮箱队列）→ `StreamAsync` 支持下一 step 注入 → 宿主 `mode:"steer"` 路由 + 事件投影 | ⬜ |

## 四、B9 验收（四列）

真机 exe 环境（ADR 0080）：
- 隔离 home `%TEMP%\opencode\dsh-m2c2-e2e\home`（最小 `settings.json`：`vendor.stub → http://127.0.0.1:8899`，`STUB_API_KEY=stub-key`），
  工作区 `%TEMP%\opencode\dsh-m2c2-e2e\ws-m2c2`（`JCC_DIRECTORY_PICK` 注入），WebView2 目录 `...\webview2-b9`，
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`。
- 启动：`Start-Process artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe`（默认即 DSH 前端）→ `node dumps/wait-webui.mjs`。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `TranscriptService.ResolveTranscriptFile`（`.jsonl` 优先、`.json` 回退，ADR 0144） | `dsh-webui-history.mjs` 播种 `sessions/history-e2e/transcript.jsonl` → 引擎读回 → 快照含 `user/message`+`assistant/message` → 界面渲染历史问答 | ✅ | ✅ ALL_PASS（16 项）：`快照历史页非空且光标为最后 seq`、`历史页含用户与助手消息事件 turn/start,step/start,user/message,assistant/message,step/end,turn/end`、`界面渲染出历史问答` 全过 |
| 引擎写侧 JSONL 落盘 | `dsh-webui-prompt.mjs` 读 `sessions/<id>/transcript.jsonl` 断言用户/助手文本 | ✅ | ✅ ALL_PASS（23 项）：`用户消息落盘到 transcript.jsonl`、`助手回复落盘到 transcript.jsonl bytes=974`、`sessions/prompt 受理回执 accepted`、`无流式错误帧` |
| exe 路径 / 启动方式对齐（`NativeGui.Host.exe`，默认 DSH 面） | `launch-webui.mjs`、`wait-webui.mjs` | ✅ | ✅ `GUI_READY`（超时由 15s 提到 45s：引擎组装 `CreateGuiSessionAsync` 实测 ~7.6s + WebView2 就绪，CDP 端口 ~17–20s 才开） |
| 回归基线（改协议层后必跑） | `tool/cancel/queue/reload` 四脚本 | ✅ | ✅ `dsh-webui-tool.mjs` ALL_PASS（工具卡片 `call_stub_1` state=error）、`dsh-webui-cancel.mjs` ALL_PASS（turn/end aborted）、`dsh-webui-queue.mjs` ALL_PASS（两条 queue 串行两轮）、`dsh-webui-reload.mjs` ALL_PASS（`session/page 页尾对齐 throughSeq=27`） |
| `make-jcc-home-messages.mjs` 双写 `gui.json`（旧存储回退）+ `transcript.jsonl`（引擎入口） | 自研面遗留夹具 / `dsh-jcc-home-messages` 目录 | ✅ | ⬜ 未跑（依赖已废弃的自研 WebUI 面，随 M3 删除再定去留） |

### B9 遗留观察（不影响验收，记录备查）

- **脚本无节制重载导致偶发 UI flake**：`dsh-webui-queue.mjs` 首次运行报 `element was detached from the DOM, retrying` 超时，重跑即 ALL_PASS。根因是点「发送」瞬间队列重排使节点被 React 重挂载，`page.click` 重试耗尽。属 E2E 脚本侧稳健性问题（工具目录 gitignored），后续可改为 `page.getByRole('textbox').fill + press('Enter')` 或 `locator.click({force:true})`。
- **窗口尺寸被外部改小会让断言假失败**：实测 GUI 窗口曾从 1440×900 缩到 533×458，侧栏折叠成 56px rail，`工作区 ws-m2c2 就绪` 断言随之超时（与代码无关）。E2E 前应确认窗口尺寸；必要时脚本显式断言 `window.innerWidth >= 1000`。


## 五、B10 验收（四列）

验收脚本 `dumps/dsh-webui-approval.mjs`（工具目录，gitignored），同 §四 的隔离 home / 工作区 / CDP 环境，另加两项前置：

- `home/settings.json`：`current.permissions.ask = ["read(approval-probe.txt)"]`（`PermissionConfig.AskRules` 在 **引擎启动时** 由 `LoadPermissionsFromSettingsAsync` 一次性装入，改后必须重启 exe）。
- 工作区 `ws-m2c2/approval-probe.txt`，内容含哨兵串 `APPROVAL_PROBE_CONTENT_7f3a`。

桩新增分支：提示词含 `USE_APPROVAL_TOOL` 时返回**真实** `read` 工具调用（`{"file_path": <STUB_APPROVAL_PATH 绝对路径>}`），取代旧的假工具 `StubTool`——这样才会走引擎的路径权限守卫而不是「工具不存在」错误分支。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `PathPermissionMiddleware` 步骤4（`MatchRule` 命中 `Read+Ask` 规则 → `Ask`），早于步骤6「工作目录内 allow」 | 桩返回 `read(approval-probe.txt)` → 引擎在 `auto` 模式下仍判定 `PendingConfirmation` | ✅ | ✅ `request.toolName = read`、`request.reason = 读取路径 …\approval-probe.txt 需要用户确认。` |
| `DshApprovalBroker.RequestAsync`（构造 waterfall 帧，前端硬校验「必须且只能含 type/event/eventId/agentId/request」） | `$events` 流下发 `approval/request` | ✅ | ✅ 帧键集实测 `agentId,event,eventId,request,type`；`request` 不含 `agent`/`signal`；`agentId` = 会话 ID（`DshTurnRunner.SetAgent`） |
| `dsh-client-ui-approval` 面板渲染（`data-approval-key` + 「等待审批」条） | composer 内嵌审批面板 | ✅ | ✅ 出现 `data-approval-key=approval:1|2`，文案含「允许一次」「拒绝」；截图 `dumps/dsh-webui-approval-panel-{allow,reject}.png` |
| `$events/result` → `DshApprovalBroker.SettleAsync`（`allowed-once`=Allow，其余=Deny，clientId 校验 + 幂等） | 前端点按钮回帖 | ✅ | ✅ 两条路径都实测：`拒绝` → `tool/result.isError=true`，文案 `Error: 用户拒绝工具执行: read`，哨兵串**未**进事件流/界面；`允许一次` → `tool/result` 内容即文件内容（含哨兵串），界面 `[data-tool="read"][data-state="ok"]` |
| `GuiPermissionConfirmationHandler.ApproveDecision`（Allow → `ApproveToolTemporarily("read", 1min)` → 重跑管线放行） | 引擎重试管线，工具**真正执行** | ✅ | ✅ 允许后工具真实读到文件（哨兵串出现在 `tool/result` 与事件流）；两轮均以 `turn/end{kind:"completed"}` 收尾；`ALL_PASS`（33 项，连续两次确定性通过） |

### B10 遗留观察（不影响验收，记录备查）

- **批准顺序有副作用，E2E 必须先拒绝后允许**：`ApproveToolTemporarily(toolName, 1min)` 按**工具名**缓存，先允许再发第二轮会让第二轮不再弹面板（被临时批准短路）。脚本因此固定 `reject → allow` 顺序；跨轮验证应用不同工具名或重启 exe。
- **审批面板只关 UI 不等待 BP 层结果**：点击后面板立即消失（`aria-busy` 期间），断言必须转到 `tool/result` 事件而不是面板本身。
- **`approval/asked` / `approval/decided` 审计事件未落会话日志**：`DshApprovalBroker` 当时只发 `$events` 瞬时帧，不回写 `approval/*` 会话事件；DSH 参考实现（`dsh-user-approval`）会在轮内落审计事件便于历史回放。**B12 已补齐**（见 §六）。
- **测试桩触发判定改为「标记出现在最后一次 `"role":"tool"` 之后」**：原判定 `!body.includes('"role":"tool"')` 在多轮会话里会让第二轮起不再触发（历史含旧工具消息），改用 `lastIndexOf` 比较，`USE_TOOL` / `USE_APPROVAL_TOOL` 同时受益。


<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 先修 E2E 脚本再推进新能力 -->
<!-- 原因: 脚本断言过期会让后续每批实现失去可信回归手段；审批/steer 都需要 script 级验证 -->
<!-- 替代方案: 先做 steer 引擎扩展（风险高且无回归护栏）—— 暂不采用 -->
<!-- 验证: TASK060 建档，B9 起步 -->

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: B10 用「真实 read 工具 + settings ask 规则」触发审批，而非测试专用 seam / 仅协议层断言 -->
<!-- 原因: 用户选定；真实守卫链路（auto 模式 + PathPermissionMiddleware 步骤4）才算端到端验收；假工具只会命中「工具不存在」错误分支 -->
<!-- 替代方案: ①引擎注入测试 seam ②只在协议层回放 waterfall 帧 —— 都绕过了真实权限管线，暂不采用 -->
<!-- 验证: dumps/dsh-webui-approval.mjs ALL_PASS（33 项，连续两次），截图存 dumps/ -->


## 六、B12 验收（四列）

补齐 §五 遗留缺口：审批**审计事件**落会话日志，使历史回放与 DSH 参考实现（`dsh-user-approval/lib/index.js`：轮内 `approval/asked` → 等结果 → `approval/decided`）一致。
同一验收脚本 `dumps/dsh-webui-approval.mjs`（同 §五 的隔离 home / 工作区 / 桩分支）追加断言：两轮各 7 项审计断言 + 刷新重载 4 项回放断言，脚本现共 **48 项**。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `DshApprovalAskedData` / `DshApprovalDecidedData` DTO（`ToolName`/`CallId`/`Reason`/`Outcome`，`DshJsonContext` camelCase） | `approval/asked` / `approval/decided` 会话事件的序列化 | ✅ | ✅ 日志实测形状 `{"id":"11123f8d…","toolName":"read","callId":null,"reason":"读取路径 …\approval-probe.txt 需要用户确认。"}` 与 `{"id":"11123f8d…","outcome":"rejected"}`；`id` 非空、`decided.id == asked.id` |
| `DshSessionStreamHub.PublishElementAsync`（分配 seq + `DshSessionJournal` 落盘 + 推送订阅者） | 审计事件写会话日志并进入 `$events` 流 | ✅ | ✅ 事件流实测 `turn/start,step/start,user/message,tool/call,approval/asked,approval/decided,tool/result,assistant/message,step/end,turn/end` |
| `DshApprovalBroker.RequestAsync` / `SettleAsync` / `AppendAuditAsync`（`asked` 在发帧前落，`decided` 由 `TrySetResult` 赢家只落一次） | 每轮审批恰好一对审计事件 | ✅ | ✅ `asked` 紧跟 `tool/call`、`decided` 早于 `tool/result`；拒绝轮 `outcome=rejected`、允许轮 `outcome=allowed-once` |
| `DshTurnRunner.FinishTurnAsync`（**先** `Approvals.CloseTurnAsync` 结清审计对，**再** `EndTurnAsync` 落 `step/end`+`turn/end`），三个分支（completed/aborted/error）统一走它，`finally` 不再关轮 | 审计对必须被本轮 `hasOpenTurn` 包住（DSH invariant 否则抛 "outside an open turn"），取消/失败路径同样适用 | ✅ | ✅ 完成轮 `turn/start(=0) < asked(=4) < decided(=5) < turn/end(=9)`；取消轮改前 `decided(=8) > turn/end(=7)`（违规）→ 改后 `decided(=6) < turn/end(=8)` |
| `DshSessionStore.SnapshotAsync`（不过滤事件类型）+ 前端历史渲染 | 刷新后审计事件随快照回放 | ✅ | ✅ 刷新后回放到 `approval/asked`、`approval/decided + allowed-once`、以及 `rejected` 判定，界面同时渲染历史问答 |
| 单测 `DshApprovalBrokerTests` 新增 7 例（词表映射 / 无开放轮次跳过 / 监听者抛错→`unavailable` / 关轮→`cancelled` 且闭环 / 关轮等待断流补写屏障） | 审计语义的确定性验证（不依赖真机） | ✅ | ✅ `dotnet test native_gui/tests/join_code_gui.tests` = **277 通过 / 0 失败**（原 270 + 7）；真机 `ALL_PASS`（48/48，连跑三次） |

### B12 边界验收（detach / cancel，脚本 `dumps/dsh-webui-approval-edges.mjs`）

非正常路径同样要求「询问与判定成对、且被本轮包住」。合并「最后一次 `snapshot.records`」与「live `$events` 事件帧」后断言 `turn/start < asked < decided < turn/end`、`decided.id == asked.id`、`outcome` 词表、工具 `isError`、哨兵未入流。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `DshApprovalBroker.Detach`（`$events` 断开 → 挂起请求 Deny + `unavailable` 审计）+ `_detachedAudit` 屏障（`Task.WhenAll` 累积 fire-and-forget 补写） | detach 路径：前端不回答直接刷新页面 | ✅ | ✅ `ALL_PASS`：`outcome=unavailable`、工具 `isError=true`、哨兵未入流、`0/4/5/9` 轮内闭合 |
| `CloseTurnAsync` 排空 `_detachedAudit` 屏障（同步 helper 取锁，规避 async 持锁 JCC4001） | 关轮返回时保证 `decided` 已落库，不会晚于 `turn/end` | ✅ | ✅ `ALL_PASS`：cancel 路径 `outcome=cancelled`、`turn/end{kind:"aborted",reason:{kind:"user"}}`、`running=false`、`0/4/6/8` 轮内闭合 |
| 红→绿证据 | 缺陷驱动（TDD 铁律2） | ✅ | ✅ 先红：新单测 `CloseTurn_WaitsForDetachedAudit_SoPairStillLandsInsideTurn` 失败（`失败: 1，通过: 17，总计: 18`）；后绿：同过滤集 `18/18`，全量 `277/0` |

回归（本轮改动后全跑）：`approval` 48/48、`edges` ALL_PASS、`prompt`/`tool`/`queue`/`cancel`/`reload`/`history` 全 `ALL_PASS`；`NativeGui.Host` Debug 编译 0 警告。

### B12 遗留观察（不影响验收，记录备查）

- **审计写入失败不阻塞审批**：`AppendAuditAsync` 只记日志（无会话 ID / 无 sink / 序列化异常都吞掉），放行流程永远不被日志问题拖死。
- **孤儿审批后台补写**：`$events` 断开时挂起请求结算为 `unavailable`，审计补写 fire-and-forget（释放函数禁阻塞，ADR 0093）；但**关轮必须排空它**，否则 `decided` 会落到 `turn/end` 之后（本轮修复的正是这条）。
- **取消路径比正常路径更易暴露顺序缺陷**：取消时工具结果先回（`tool/result`）、轮次立即收尾，若关轮晚于 `turn/end` 则必然违规；正常路径因工具执行耗时掩盖了竞态，故必须单独造 cancel/detach 边界脚本。
- **E2E 选工作区必须重载页面（脚本侧）**：前端点选工作区后 composer 阶段不当帧刷新（实测停在 `data-phase=plain`），`ensureWorkspace` 统一为「点菜单项 → `page.reload()` → 等可编辑」；并容错「异步恢复会话时选择入口闪现又消失（点击途中 `element was detached`）」。自查发现 `dsh-webui-prompt.mjs` 重载后未等输入框就断言（无 `until` 的竞态），已改为确定性等待。
- **`dsh-webui-reload.mjs` 去掉隐式耦合**：原脚本假设「侧栏第一行 = 上一条脚本留下的会话」，会话数一多就漂移。改为自建内容（普通一轮 + `USE_TOOL` 一轮）后再刷新断言，并可独立运行（自带 `ensureStub`）；默认会话还原已渲染历史内容时不再要求点侧栏行。
- **窗口尺寸漂移是所有侧栏断言的共同假失败源**：真实窗口实测从 1440×900 漂到 575×425 时侧栏折叠成 rail（`div[class*=sessionRow]` 数为 0），`dsh-webui-history.mjs` 因此连续两次假失败。已在 `_e2e.mjs#connectDshPage` 统一 `page.setViewportSize({1440,900})` 固定视口，并把 `history.mjs` 从自建 CDP 连接迁到共享连接；固定 5s 等待改为等「点行后的打开快照」到达。
- **单脚本执行必须限时**：脚本收尾 `cdp.close()` 偶发不返回（工作已完成但进程不退出），运行方对每个脚本设硬超时（≤3 分钟）并读日志判定，禁止无上限等待。


<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 审批审计事件由宿主侧 DshApprovalBroker 写入会话流（Audit 委托注入 PublishElementAsync），而非引擎侧落盘 -->
<!-- 原因: 审批是宿主与前端之间的协议对象（waterfall 帧 + $events/result 回帖），引擎只看到"工具需要确认"；审计必须与帧同一时刻产生才能保证 asked/decided 成对且轮内闭合 -->
<!-- 替代方案: ①引擎在权限拦截层落事件（会与宿主的 eventId 体系割裂，且无法表达 unavailable/cancelled）②只在日志文件写不推进事件流（刷新回放看不到）—— 都不采用 -->
<!-- 验证: 真机 ALL_PASS（48/48 两次）+ 单测 276/0；审计对轮内闭合断言 turn/start < asked < decided < turn/end -->

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 轮次收尾顺序固化为「先 CloseTurnAsync（结算 + 排空断流补写屏障）再 EndTurnAsync」，通过 DshTurnRunner.FinishTurnAsync 统一三个结束分支 -->
<!-- 原因: 审计对必须被本轮包住；取消路径实测 decided(=8) 晚于 turn/end(=7)。根因是 fire-and-forget 的 detach 补写与异常分支直接在 EndTurnAsync 前收尾，finally 里的关轮太晚。抽 FinishTurnAsync 让"关轮"成为结束轮次的唯一入口，顺序在类型层面不可颠倒 -->
<!-- 替代方案: ①只在 finally 提前关轮（异常分支已发 turn/end，仍违规）②审计改为同步阻塞等待（拖慢放行路径）③给审计加时间戳后置排序（掩盖问题且不符合 DSH 事件即时语义）—— 都不采用 -->
<!-- 验证: 边界脚本 detach/cancel 全 ALL_PASS（0/4/5/9 与 0/4/6/8）；单测 277/0；approval 48/48；prompt/tool/queue/cancel/reload/history 回归全绿 -->

## 七、B11 steer（轮内注入）计划与验收

DSH 语义（权威源 `dsh-agent-loop/lib/index.js:998-1006`、`:1061`、`dsh-api-session-controller/lib/index.js:882`）与落地方案见 [ADR 0146](../../proposed/adr/0146-steer-in-turn-injection-mailbox.md)：**邮箱投递（宿主）→ 步首 drain（引擎）→ 轮内 `user/message` 事件（宿主投影）**，且「模型本步无工具调用但邮箱非空」不结束轮次。

### 批次步骤（红 → 实现 → 编译 → 绿 → 文档 → commit）

| 步骤 | 内容 | 状态 |
|------|------|------|
| B11-a | 契约与 ADR：`ISteerInputMailbox` + `MessageOriginKind.SteerInput` + `ChatStreamEventType.SteerInput`（ADR 0146 proposed） | ✅ |
| B11-b | E2E 红：`dsh-webui-steer.mjs`（桩按注入文本应答；断言受理、轮内 `user/message`、单轮、下一步请求含注入文本） | ✅ 红已确认 |
| B11-c | 引擎单元红→绿：步首 drain 进下一次请求 + 有待注入不收尾 | ✅ 单测 2 例（红：CS0246/CS1739/CS0117 → 绿 9/9） |
| B11-d | 宿主路由绿：`session/prompt mode=steer`（运行中受理 / 空闲回落开轮）+ `updateQueue action.kind=steer` 升级 | ✅ WebHost 单测 142/0 |
| B11-e | 全量回归（approval/edges/prompt/tool/queue/cancel/reload/history）+ 文档四列表 + commit | ✅ 9 脚本全 ALL_PASS |

### B11-b 红灯证据（真机 exe + 桩，非 mock）

E2E 场景（`dumps/dsh-webui-steer.mjs`，桩加两个 marker）：

1. 桩 `USE_STEER_WAIT`：工具结果之后的这一步**挂起 6s**（`STUB_STEER_HOLD_MS`）再回一句**无工具调用**的正文 —— 制造确定性的「轮次仍在运行」窗口，同时天然命中「本步无工具调用但有待注入」分支。
2. 轮内 `session/prompt {mode:"steer", content:[STEER_MARK ...]}`。
3. 桩 `STEER_MARK`：请求体在**最后一次工具结果之后**出现注入文本时回 `steer 已收到：轮内注入成功`（证明确实是注入文本驱动了下一步）。

红灯矩阵（`FAILED 9`，脚本自行退出）：

| 断言 | 结果 | 实测 |
|------|------|------|
| `session/prompt mode=steer` 受理 | ❌ | `{"code":"session/busy","message":"会话 … 上一轮仍在生成；… 排队请用 mode=queue"}` |
| 得到 accepted（非 `steer-unavailable`/`busy`） | ❌ | `session/busy` |
| 本轮收尾 `turn/end` | ✅ | `turn/start,step/start,user/message,tool/call,tool/result,assistant/message,step/end,turn/end` |
| 注入文本进入事件流 | ❌ | 无 `STEER_MARK` |
| 注入未开启新轮次（单 `turn/start`） | ✅ | `turn/start x1` |
| 注入落轮内 `user/message`（`surfaceOp=append`） | ❌ | `index=-1` |
| 注入在工具结果之后 / 最终回复之前 | ❌ | `tool/result=4 steer=-1 ack=-1` |
| 最终回复来自注入分支 | ❌ | 无 ack |
| 界面渲染注入文本 | ❌ | 无 |

关键旁证：桩日志本轮只有 **2 次** `/chat/completions` 请求（无第三次带 `STEER_MARK` 的请求）→ 注入从未抵达引擎，问题在宿主受理层而非事件投影层。

**结论（对齐 DSH 的差距）**：宿主把 `mode:"steer"` 当成「非 queue 的普通 prompt」落到 `session/busy`，既没受理也没按 DSH 语义投递；引擎侧完全没有轮内注入通道。

### E2E 工具链修正（三次试跑踩出来的）

1. **红灯脚本禁把「预期为红」的断言写成会抛错的 `until`** —— 第一个不满足就抛异常中断，后半段报告全丢，看起来像「卡住」。改为有界软等待 `waitFor()`（内部 catch `until` 超时，返回 bool）+ `check()`，一次运行输出完整矩阵；整脚本再套 `try/catch` → `check('流程无异常中断')`，保证必定走到 `finish()`/`process.exit()`。
2. **桩进程必须先按命令行清理再启动** —— `_e2e.mjs#ensureStub` 无条件 `spawn` 新桩，若旧桩仍占 8899，新桩静默 `EADDRINUSE` 而探活仍通过 → 跑的就是**旧代码**（改了 marker 却不生效）。跑前先 `Get-CimInstance Win32_Process` 匹配 `stub-openai-server.mjs` 杀掉。
3. **exe 必须由「短 bash 调用」启动** —— 单次 bash 超时被掐时整个进程树（含 `Start-Process` 的 exe）一起死；把「起 exe + wait-webui」与「跑 E2E」拆成两次调用，单次都在限时内结束。
4. **拿会话 ID 别依赖 `session/list` 的 `running`** —— 该字段恒为 `false`（`DshSessionStore.ProjectAsync` 里写死），改为取 `session/follow` 快照的 `header.id`，回退 `session/list` 首条。

### B11-e 绿灯证据（真机 exe + 桩，非 mock）

| 断言 | 结果 | 实测 |
|------|------|------|
| `session/prompt mode=steer` 受理 | ✅ | `{"accepted":true}` |
| 注入文本进入事件流（驱动下一步） | ✅ | 桩回 `steer 已收到：轮内注入成功` |
| 注入未开启新轮次 | ✅ | `turn/start x1`，`RunningTurn=1` |
| 注入落轮内 `user/message`（`surfaceOp=append`） | ✅ | `index=5` |
| 注入在工具结果之后 / 最终回复之前 | ✅ | `tool/result=4 < steer=5 < ack=6 < turn/end=8` |
| 界面渲染注入文本 | ✅ | 侧栏/气泡均出现注入文本 |

- 事件序：`turn/start,step/start,user/message,tool/call,tool/result,user/message,assistant/message,step/end,turn/end`；`dsh-webui-steer.mjs` **ALL_PASS，9.6s**（`lifecycle` 看门狗：单步 10s / 总预算 60s）。
- 单元：`QueryLoopMiddlewareTests` 9/9（`drain→AddUserMessage(SteerInput)→下一步 LLM`、`无工具调用但邮箱非空→不收尾`）。
- 宿主管辖：`DshSteerInputTests` 3 例（运行中注入不新开轮 / 空闲回落开轮 / 引擎无邮箱 → `session/steer-unavailable` 且不排队）+ `DshUpdateQueueTests` steer 正反 2 例（投递即出队 / 不可收时消息留在队列）。
- 全量回归（同一 exe + 桩）：`prompt / tool / queue / cancel / reload / history / approval / approval-edges` 全 `ALL_PASS`。

### 验收四列表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `ISteerInputMailbox`（按会话寻址的 FIFO 邮箱，Actor 邮箱模型；`SteerInputMailbox` 单例 + 同步 lock） | 宿主投递（`JccChatSession.TrySteerInput`）/ 引擎步首 drain | ✅ | ✅ `dsh-webui-steer.mjs` ALL_PASS + `DshSteerInputTests`（[ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 手动 exe 真机） |
| `QueryLoopMiddleware` 步首 drain → `AddUserMessageAsync(SteerInput)` + `ChatStreamEventType.SteerInput` | 下一步 LLM 请求携带注入文本；宿主投影 `user/message` | ✅ | ✅ `QueryLoopMiddlewareTests` 9/9 + 真机事件 `steer=5`（注入驱动桩回 ack） |
| 收尾分支「邮箱非空则继续下一步」（对齐 DSH `next-step` 非空不结束轮） | 模型最后一步收到 steer 仍继续 | ✅ | ✅ `InvokeAsync_TextOnlyResponseButSteerPending_DoesNotEndTurn` + 真机单 `turn/start` 内含注入 |
| `DshTurnRunner`/`DshRpcDispatcher` steer 路由（替换 `session/steer-unavailable` 的旧拒绝语义） | 前端/协议 `session/prompt mode=steer`、`updateQueue action.kind=steer` | ✅ | ✅ WebHost 单测 142/0（含正反例）+ `dsh-webui-queue.mjs` ALL_PASS |

### B11 遗留观察（不影响验收，记录备查）

1. **测试项目已重组**：`native_gui/tests/join_code_gui.tests` → `native_gui/tests/native_gui.core.tests`（`NativeGui.Core.Tests.csproj`，程序集名仍 `JoinCode.Gui.Tests`）。本文档历史段落里的旧路径保留原样，实际运行请用新路径。
2. **`session/list` 的 `Running` 恒 `false`**（`DshSessionStore.ProjectAsync` 写死），别用它判断在跑，取 `Streams.RunningTurn` 或 `session/follow` 快照。
3. **`artifacts/bin` 曾整目录消失 / 宿主进程意外退出**（两次，原因未查明，疑似外部清理或用户侧操作）：表现是 E2E 报 `Target page, context or browser has been closed` 或找不到 exe。处置：重新 `dotnet build native_gui/apps/NativeGui.Host/NativeGui.Host.csproj -c Debug` + 重启 exe，同一调用里 `Test-Path` 确认。
4. **转录落盘与界面渲染无强顺序**：`dsh-webui-prompt.mjs` 曾出现「界面已渲染但 `transcript.jsonl` 还没写完」的假红（`bytes=871`）。已改为 `until` 轮询文件内容（禁固定 sleep）。
