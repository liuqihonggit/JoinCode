# TASK058 — DSH 与引擎深度对齐（相同 DTO 原则）

> ADR：[0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)（延续 TASK053 M2-d/M3 未竟项）
> 目标：DSH 前端假定的宿主能力，逐项对齐到自有引擎真实数据源；宿主层直接消费引擎 DTO，消除平行存储与双源定义；GUI 作为唯一展示面。
> 执行方式：按批次推进，每批走「红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交」。

## 一、对齐差距清单（2026-10-21 双路调研结论）

### 已对齐 ✅（真引擎数据，勿动）

会话核心八件套（`list/create/rename/prompt[text+queue]/cancel/page/projections/modelCatalog`）+ workspace 全套 11 方法 + 4 流端点（`$events/session/follow/session/control/workspace/follow`）+ `directoryPicker/pick`。

### P0 — 静默断裂（功能已坏且用户不可见）

| # | 断裂点 | 现状 | 引擎闲置能力 |
|---|--------|------|--------------|
| 1 | 权限确认链 | DSH 宿主未注入 `PermissionConfirmationHandler` → `GuiPermissionConfirmationHandler.ConfirmAsync` fail-closed 静默 Deny，前端权限对话框永不弹出 | handler 已实现（旧 WebUI 消费）；`approval/request` waterfall 事件白名单前端已定义 |
| 2 | 模型切换 | `session/selectModel` 未实现；`ModelSelection.Next` 恒 null；模型选择器只读 | `SetModelAsync/SetVendorAsync` 闲置 |
| 3 | 设置双源 | `DshSettingsStore` 进程内内存假 schema，重启即丢，与 settings.json 零互通 | `IConfigurationService` + `SettingsJson` DTO |
| 4 | 权限模式 | `permissionPresets/catalog` 硬编码且选择无路由；宿主三元词汇与引擎 `PermissionMode` 枚举无映射 | `SetPermissionModeAsync` |
| 5 | 流事件收窄 | `DshTurnRunner` 只消费 4/13 类：Thinking/ToolProgress/`Complete.Usage`/AgentStarted/Finished 全丢弃 | `ChatStreamEvent` 枚举齐全 |

### P1 — 功能未实现但有引擎数据可读

| # | 功能 | 现状 | 引擎闲置能力 |
|---|------|------|--------------|
| 6 | 斜杠命令 | `commands/list` 空数组、`commands/execute` 未实现 | `GetAvailableSlashCommands` + `ExecuteSlashCommandAsync` |
| 7 | 子代理 | 15+ 接口成员零暴露；`session/follow` 子代理地址显式拒绝；AgentStarted/Finished 流事件丢弃 | `GetBackgroundAgentsAsync/InterruptSubAgentAsync/…` |
| 8 | 会话管理 | `session/fork`、清空/回退无 | `ClearHistoryAsync/RewindLastTurnAsync` |
| 9 | Provider 列表 | `llm/listProviders` 空数组 | `VendorModelMap` |
| 10 | 逐字流式 | `assistant-stream` 恒 revision 0 | TASK043 已登记（引擎侧缺陷） |
| 11 | steer | `mode≠queue` 返回 `session/busy` | 引擎未暴露轮内注入（0945d7d32 前提记录） |

### P2 — 大块新功能

| # | 功能 | 状态 |
|---|------|------|
| 12 | 文件树 workspaceFiles/list\|read\|readBytes\|stat | ✅ 真机（弹栈式防穿越+version 合成；changes 流深化项） |
| 13 | 附件 fileUploads/upload + session/attachment + uploadFileBinary 直传 | ✅ 真机（魔数探测+四格式尺寸头解析；prompt file part 消费属引擎多模态深化） |
| 14 | 队列 session/updateQueue（edit/remove） | ✅ 单元 4/4+真机契约（steer 诚实返回 steer-unavailable；队列可见性 inbox 投影深化） |
| 15 | 终端（10 方法） | ⬜ 需引擎大扩展（ConPTY/屏幕缓冲/多 shell），另行立项 |
| 16 | goals（7 方法+事件） | ⬜ 需引擎中扩展（clock/goal 生命周期模型映射+revision CAS 适配），另行立项 |
| 17 | steer / prompt mode:"steer" | ⬜ 需引擎大扩展（StreamAsync 轮内注入点），另行立项 |
| 18 | workspaceFiles/changes 流 | ⬜ watcher 桥接深化项 |

### 不接 ❌（DSH 在线服务，与本项目无关）
account（11）、pluginManager（12）、dynamicCordisRunner（9）、productAnalytics、schedule、officeToPdf —— 保持 `unimplemented` 引导错误。

## 二、DTO 统一原则（决策）

1. **宿主直接消费引擎 DTO**：设置读写走引擎 `IConfigurationService`/`SettingsJson`；权限预设以引擎 `PermissionMode` 枚举为唯一数据源（`[EnumValue]` 扩展），宿主仅做枚举→前端词汇的一次性投影，禁止反向维护第二套枚举。
2. **事件投影单点**：`ChatStreamEvent` → DSH 事件的映射只在 `DshTurnRunner`/`DshTranscriptProjector` 单点扩展，禁止在分发器散落 ad-hoc 转换。
3. **消除平行存储**：`DshSettingsStore` 内存假数据退役，`DshModelCatalogSource` 的 settings.json 直读与引擎 attach 双来源保留但补 `RefreshVendorModelMap` 联动。
4. JSON 一律 DTO + `JsonContext` 源生成（ADR 0133）。

## 三、批次计划（每批一次 commit）

| 批次 | 内容 | 状态 |
|------|------|------|
| B1 | 权限确认链闭环：注入 handler + `approval/request` waterfall + `$events/result` 回帖 + 权限预设接 `PermissionMode` 映射与 `SetPermissionModeAsync` 路由 | ✅ 单元+冒烟（选择路由落 B3 settings 统一；真机审批弹窗待手动验收） |
| B2 | 模型切换：`session/selectModel` → `SetModelAsync/SetVendorAsync` + `llm/listProviders` 接 `VendorModelMap` | ✅ 单元（引擎分支真机验收） |
| B3 | 设置统一：`DshSettingsStore` → dsh-settings.json 持久化 + permission/defaultPreset 路由 `SetPermissionModeAsync` | ✅ 单元 12/12 |
| B4 | 流事件扩展：Thinking → reasoning 块 + `Complete.Usage` → data.usage 四桶（ToolProgress 暂缓） | ✅ 单元 4/4 |
| B5 | 斜杠命令：`commands/list` 投影 [ChatCommand] 目录 + `commands/execute` 委托 SlashCommandRunner | ✅ 单元 5/5 |
| B6 | 子代理流事件：AgentStarted/Finished → tool-workflow 事件族 + subagentCatalog 投影回放（B8） | ✅ 单元 10/10 |
| B7 | 会话管理：`session/fork` 复制 meta/转录/journal（清空/回退无前端 RPC 消费点，不做） | ✅ 单元 3/3 |

### 后续修复（真机验收期间发现）

- **2026-10-21 `4e4088744`**：#439 纯指针形状引发 GUI 启动期模型目录 NRE（`SettingsJson.Vendor` 反序列化为 null）。修复 = 删除 GUI 侧第二套 settings.json 手写解析，委托 guard 公共链 `SettingsLoader.LoadUserSettingsFromDirAsync`（指针 + providers_llm/current 子文件单点合并）。**架构原则（用户确认）：宿主配置解析必须在公共引擎层单点，GUI/TUI 同源消费，配置路径/形状再变宿主零改动。**

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| B1 `DshApprovalBroker`（waterfall 帧/挂起结算/断开 fail-closed）+ `DshWebUiWindow.AttachEngineAsync` 注入 `PermissionConfirmationHandler` + `$events/result` 路由 + `permissionPresets/catalog` 枚举映射（`PermissionMode` 唯一数据源） | 前端权限对话框弹窗（approval/request）/权限预设选择器 | ✅ 单测 11/11 + 冒烟 `$events` attach | ✅ 真机 RPC：catalog 五值枚举投影（plan/auto/ask/bypass/unattended+defaultPreset=auto）；审批弹窗需 UI 人工点击（`$events` 流已挂 approval 桥，留用户交互验证） |
| B2 `session/selectModel` 路由 + `ModelSelection.Next` 真值 + `llm/listProviders` | 模型选择器切换/Provider 列表 | ✅ 单测 4/4 | ✅ 真机：listProviders 6 家；selectModel 引擎真实切换（sensenova 回显生效） |
| B3 `DshSettingsStore` 改造读写引擎 settings.json | 前端设置页（general 等） | ✅ 单测 12/12 | ✅ 真机：describe/mutate 闭环（写 plan→落盘→复原 auto，revision 0→1→2，值域校验生效） |
| B4 `DshTurnRunner` 事件映射扩展（5 类） | 思考块/工具进度/子代理卡片/token 统计 | ✅ 单测 4/4 | ✅ 真机：真实对话轮 journal 验证 — assistant/message content 块 + data.usage 四桶（142442 入/21 出/缓存 0）+ 事件序列 turn/step/user/assistant 完整 |
| B5 `commands/list|execute` 路由 | 斜杠命令补全与执行 | ✅ 单测 5/5 | ✅ 真机：list 84 条（GeneratedSlashCommandCatalog 兜底修复）；/agents 真实执行返回代理列表 |
| B6 agents/interrupt/follow 子代理 | 子代理面板/轨迹 | ✅ 单测 7/7 | ✅ 机制真机：journal 事件管道真实落盘（本轮无子代理触发，投影逻辑单测覆盖） |
| B7 fork/clear/rewind | 会话菜单操作 | ✅ 单测 4/4 | ✅ 真机：fork 无 meta 历史会话成功分叉（容错修复）；clear/rewind 无前端 RPC 消费点不做 |
| B8 DshSubagentCatalogProjector journal 回放 subagentCatalog（空数组消除前端 loading 挂起） | 子代理面板目录 | ✅ 单元 3/3 | ✅ 真机：session/list 投影 subagentCatalog:[] |

## 风险与前提

- journal 双源「CLI 后写不可见」窗口：属引擎事件总线下沉课题（TASK053 已预警），本任务不处理，另行立项。
- `assistant-stream` 逐字流式依赖 TASK043（引擎流式缓冲缺陷）修复，本任务 B4 只做事件级增量。
- steer 依赖引擎暴露轮内注入接口（0945d7d32），本任务不实现。

### 真机验收记录（2026-10-21，NativeGui.Host exe + curl RPC）

- B2 修复：selectModel 信封为 `{request:{sessionId,provider,model}}`（错误引导消息实测有效）
- B5 修复：commands/list 空数组根因 = GUI 会话 DI 未注册 ISlashCommandCatalog，补 `GeneratedSlashCommandCatalog` 兜底（对齐 TUI SlashCommandSource 先例）→ 84 条
- B7 修复：历史会话只有 transcript.jsonl 无 meta.json 时 fork 误报 unknown，ForkAsync 容错构造最小 SessionInfo（新增单测 Fork_WithoutMetaFile_FallsBackToTranscriptOnlySession）
- B1 审批弹窗为 UI 人工交互项：`$events` 流已挂 approval 桥（日志 `[DSH-APPROVAL] events stream attached`），待用户在 DSH 前端发触发权限的消息实测
