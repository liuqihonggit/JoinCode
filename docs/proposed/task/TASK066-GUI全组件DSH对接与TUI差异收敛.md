# TASK066 — GUI 全组件 DSH 对接与 TUI 差异收敛

> 延续 [TASK060](TASK060-DSH剩余对齐与验收.md)（B9–B13 已验收）与 [ADR 0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)。
> 目标（对应用户三条要求）：
> 1. 完成所有 GUI 组件与 DSH 宿主对接，每个组件都有可复现的验收方式；
> 2. GUI 与 TUI 能力存在差异的，给出解决方案并完成对接；
> 3. 设计冲突时优先对齐 Claude Code。
>
> 验收方式统一为：**单元测试 + ADR 0080 手动 exe 真机 E2E（截图/事件断言，非 mock）+ 四列验收表**。

## 一、组件覆盖矩阵（按 DSH 启动清单 66 个插件）

事实源：`index.html` 的 boot entries、`dsh-api-remotes/lib/client.js` 的远程 schema、宿主 `DshRpcDispatcher` 路由表。

| 组件（插件包） | 界面面 | 所需宿主方法 | 现状 | 验收方式 |
|---|---|---|---|---|
| `dsh-client-ui-chat` / `conversation` / `renderer` | 对话流、助手逐字 | `session/prompt`、`session/follow`、`$events`、`assistantStream` 基线 | ✅ TASK058/060 | 已有 10 个 E2E 脚本 |
| `dsh-client-ui-sidebar` / `workspace` | 工作区与会话侧栏 | `workspace/*`、`session/list|page|fork` | ✅ | E2E `reload/history` |
| `dsh-client-ui-sidebar-terminal` + `dsh-api-terminal-controller` | 侧栏终端面板 | `terminal/{environment,list,shells,create,write,resize,rename,close,retain}` + `terminal/follow` 流 | ✅ ConPTY + 10 方法 + follow/retain；子进程标准句柄显式置空（不继承宿主重定向） | 单测 11 例 + `dsh-webui-terminal.mjs` RPC ALL_PASS + `dsh-webui-terminal-panel.mjs` 界面 11 项 ALL_PASS（真实 pwsh 执行 echo、cwd、无错误条） |
| `dsh-client-ui-jobs` + `dsh-api-job-controller` | 后台任务面板 | `job/list`、`job/follow` 流、`job/kill` | ✅ 后台代理投影 + rows/output/status | 单测 5 例 + `dsh-webui-jobs.mjs` ALL_PASS（opened 需存在后台代理时覆盖） |
| `dsh-client-ui-sidebar-files` + `dsh-api-workspace-files` | 文件树 | `workspaceFiles/{list,read,readBytes,stat}` + `workspaceFiles/changes` 流 | ✅ 一元 + ready/change/absent/error 帧；路径支持前端绝对路径（根内校验）；readBytes 走 multipart 二进制 | 单测 11 例 + `c5-stream-e2e.mjs` 8 项 ALL_PASS |
| `dsh-client-ui-sidebar-documentpreview` | PDF/Excel/图片预览 | `workspaceFiles/readBytes`（multipart 字节附件）+ `officeToPdf/{generation,render}`（诚实拒绝） | ✅ 资产打包 + 缓存完整性校验 + 二进制契约；PDF/Excel/图片可用，docx/pptx 转换服务未接入 → `gateway/service-unavailable` + 替代方案引导 | `dsh-webui-c5-preview.mjs` 12 项 ALL_PASS（PDF 3 页、Excel 画布渲染）+ `dsh-webui-unsupported.mjs` 真机（render 错误形状与文案） |
| `dsh-client-ui-approval` | 审批面板 | `$events` waterfall + `$events/result` + 审计事件 | ✅ TASK060 B10/B12 | `dsh-webui-approval.mjs` 48/48 |
| `dsh-client-ui-goal` | 目标条 | `goals/*` 七方法 + `goal` 会话投影 + `session/control` 实时帧 | ✅ agent 作用域七方法（create 用 `request` 信封、edit 用 `request`、clear 返回裸 `{id,revision}`、get 无目标为 undefined）；投影面无目标 JSON `null`，有目标 `{goal,roundsStarted,createdAt,updatedAt}`（不含 activation） | 单测 5 例 + `c7-goal-e2e.mjs` 真机 28 项 ALL_PASS（目标条渲染、暂停/恢复/编辑/清除、控制帧 seq 水位） |
| `dsh-client-ui-plan` | 计划面板 | `plan/mode` 事件 + `session/control` 投影 + `/plan on|off|status` | ✅ 权限预设 → 引擎 `PermissionMode` → `plan{active,pending=false}` / `permissions{currentValue}` 投影；前端 chip 点击走 `commands/execute /plan off` 宿主拦截 | 单测 8 例 + `c6-e2e.mjs` 真机 30 项（chip 出现/点击/选择器同步） |
| `dsh-client-ui-subagent` | 子代理卡片 | `subagents/{prompt,interruptByParent}`（catalog 已由 `DshSubagentCatalogProjector` 投影） | ✅ 文本投递 + 中断（image 显式拒绝并引导） | 单测 8 例 + `c6-e2e.mjs` 真机 3 项引导断言 |
| `dsh-client-ui-commands` / `input-trigger` | 斜杠命令/引用补全 | `commands/list|execute`、`fileReferences/list` | ✅ 斜杠命令 + @ 文件引用补全（索引缓存 5s、排除目录、20 条）+ `/plan` 语义接管 | `dsh-webui-prompt` 回归 + 单测 6 例 + c6 真机 |
| `dsh-client-ui-settings*`（general/models/shell/agent-loop/subagent/web-search/account/plugins） | 设置中心 + 模型页 | `settings/{describe,mutate,replace,update}` + `llm/{listProviders,listConfigurableProviders,discoverModels}` + `credentials/{describe,set,unset}` | ✅ 通用命名空间四条写/读路径；供应商虚拟命名空间（`llm-deepseek`）`describe/mutate` 直写引擎 `providers_llm/`（含 `config.json` + `models.json`），密钥经共享 `auth.json`（DPAPI）落盘并热更新引擎；通用设置页「打开配置文件」直开 `~/.jcc/dsh-settings.json`（打开前先落盘） | 单测（整段替换/顶层补丁/revision 冲突、供应商配置与模型合并、连续保存稳定版本、环境变量遮蔽拒绝移除、打开文件成功/失败引导）+ `dsh-webui-models-settings.mjs`、`dsh-webui-credentials-references.mjs`、`dsh-webui-settings-document.mjs` 真机 ALL_PASS |
| `dsh-client-ui-model-selection` | 模型选择 | `session/modelCatalog|selectModel` | ✅ | 既有 E2E |
| `dsh-client-ui-skill` | 技能面板 | `skills/list` | ✅ 引擎 `ISkillService` 映射（名称/描述/模型可调用/源路径） | 单测（RPC 4 例 + 映射 2 例）+ `c6-e2e.mjs` 真机 19 条技能 |
| `dsh-client-ui-message-feedback` | 消息反馈 | `messageFeedback/{list,put,delete}` + `sessionFeedback/record` | ✅ 逐条落盘 + 乐观并发 + 契约错误联合体；会话级反馈写 `feedback/record` 日志事件（trim 文本、可选分类、不触发模型工作） | 单测（版本冲突/备注规则/会话反馈 4 例）+ `dsh-webui-session-feedback.mjs` 真机 18 项 ALL_PASS |
| `dsh-client-ui-directory-picker-native` | 原生目录选择 | `directoryPicker/{pick,list,createDirectory}` | ✅ 原生选择 + 目录浏览（面包屑/隐藏项/1000 条截断）+ 新建目录 | 单测 13 例 + 真机 E2E |
| `dsh-client-ui-session` | 会话搜索/标题 | `session/rename`、`session/search` | ✅ 字面量搜索 + 20 条上限 + 240 字片段 | 单测（片段/hasMore）+ 真机 E2E |
| `dsh-client-ui-user-questions` | AskUserQuestion 卡片 | `$events` waterfall `user-questions/request` + `$events/result` + 结算后 `{type:"cancel",eventId}` | ✅ 引擎 `ask_user_question` → 单卡多问（单选自动翻页、多选、自填、跳过）；`userQuestions/answer` 诚实返回 false（宿主无 continued 队列），`userQuestions/attachWait` 空流（无 timed wait） | 单测 10 例 + `dsh-webui-questions.mjs` 真机 23 项 ALL_PASS（五键帧、q0/q1、单选翻页、多选 A/B、提交后工具结果 `"问题"="答案"`、cancel 帧、turn/end completed） |
| `dsh-client-ui-trajectory` / `deliverables` / `workflow-run` | 轨迹/产物/工作流 | 会话事件 + `api/changes.{summary,diff,open}` + `api/present.{host,open}` | ✅ 轨迹消费轮次事件；deliverables 每轮收口推 `workspace/changes` + 摘要/对比/原生打开；workflow-run 读轮次事件（引擎无独立工作流运行时） | 单测（改动收集 5 例）+ `c7-changes-e2e.mjs` 27 项 + `c7-trajectory-e2e.mjs` 16 项真机 ALL_PASS |
| `dsh-client-ui-plugin-manager` / `cordis` | 插件管理 | `pluginInventory/list` + `pluginManager/*`（读写诚实化）+ `pluginRegistryProbe/fastest` + `dynamicCordisRunner/{inventory,stopFromPanel,undefineFromPanel}` | ✅ 清单 `managementAvailable=false`；读路径回空清单/空注册表/null，写路径 `plugin-manager/rejected` 引导；Cordis 面板动作回 `{ok:false,reason:"plugin-missing"}` | 单测 6 例 + `dsh-webui-unsupported.mjs` 真机 ALL_PASS（插件页空态「本部署没有可管理的 profile」） |
| `dsh-api-workspace-controller` | 工作区路径应用 | `session/{openWorkspacePath,workspacePathApplications,canOpenWorkspacePath,initializeDefaultModel}` | ✅ 仅公布资源管理器处理器；能力探测返回桌面宿主；默认模型初始化按本地设置直通 | 单测（引导文案/能力探测）+ 真机 E2E |
| `dsh-client-ui-agent-preset` | 代理预设 | `agentPresets/{list,read,select}` | ✅ 公布 standard（默认项读 `agent-preset-registry.selectedDefault`） | 单测 + 真机 E2E |
| `dsh-client-ui-open-in-app` / `shortcuts` / `theme` / `brand` | 外壳能力 | 前端独立 + 主题桥 | ✅ | 截图 |

## 二、TUI ↔ GUI 差异与解决方案

| 能力 | TUI 实现 | GUI/DSH 现状 | 差异 | 解决方案（冲突优先 Claude Code） |
|---|---|---|---|---|
| 后台任务 | `BackgroundTaskPanel` + `GetBackgroundAgentsAsync` | jobs 面板无宿主数据 | GUI 缺整块 | 引擎后台代理投影为 `JobView`；`job/list` 流推 `rows`、`job/follow` 推 `output/status`、`job/kill` 映射停止代理 |
| 子代理 | `SubagentGroup` | 卡片 + `subagents/{prompt,interruptByParent}` | 无（文本投递；图片暂不支持） | 投递映射 `ForwardInputToSubAgentAsync`（queue/steer 同路径），中断映射 `InterruptSubAgentAsync`；image 块显式拒绝并引导先落盘再用路径 |
| 权限模式 | `PermissionMode` 循环（default/auto/plan） | `permissionPresets/*` + `settings/mutate` + `session/control` 投影 + `/plan` 命令 | 无 | 写路径 → `engine.SetPermissionModeAsync`；宿主计算 `plan{active,pending=false}` / `permissions{currentValue}` 并推控制帧；`/plan on|off|status` 由宿主拦截（对齐 Claude Code 语义），完成后静默落 `permission/defaultPreset` |
| 模型选择 | `ModelPickerDialog` | 已接 | 无 | 维持，补变更事件广播 |
| 斜杠命令 | `CommandPickerEngine` + `SlashCommandCatalog` | `commands/list|execute` | 无 | 维持，补 GUI 分类/隐藏字段 |
| 终端 | TUI 本体即终端 | 侧栏终端 ✅ | 无（真机 pwsh 执行命令） | ConPTY 会话管理器 + 原始输出快照回放；子进程标准句柄显式置空，宿主 stdout 重定向不再劫持提示符 |
| 会话搜索 | TUI 历史检索 | `session/search` ✅ | 无 | 扫描引擎转录的 user/assistant 消息，字面量命中取 240 字窗口，最多 20 条（第 21 条置 hasMore），与 DSH 参考实现一致 |
| 消息反馈 | 无 | DSH 有逐条反馈 + 会话级反馈（`sessionFeedback/record` → `feedback/record` 日志事件） | TUI 缺 | **以 DSH/Claude Code 为准**，GUI 保留（逐条 + 会话级均已接）；TUI 不改 |

## 三、批次计划（红 → 实现 → 编译 → 绿 → 文档 → commit）

| 批次 | 内容 | 状态 |
|---|---|---|
| C1 | 懒加载分片打包修复（terminal/pdf/excel）+ 运行时缓存完整性校验 | ✅ 已验收（终端面板真机 xterm + echo） |
| C2 | jobs：`job/list`、`job/follow` 流 + `job/kill`，映射后台代理 | ✅ 已验收（单测 5 + E2E ALL_PASS） |
| C3 | terminal：ConPTY 会话管理器 + 10 方法 + `terminal/follow`/`retain` 流 | ✅ 已验收（单测 9 + RPC/界面 E2E ALL_PASS） |
| C4 | 小型缺失方法：`session/search`、`settings/{replace,update}`、`agentPresets/{list,read,select}`、`session/{openWorkspacePath,workspacePathApplications}`、`messageFeedback/{list,put,delete}` | ✅ 已验收（单测 13 + `c4-e2e.mjs` 真机 19 项 ALL_PASS） |
| C5 | `workspaceFiles/changes` 流 + 文档预览真机验收 | ✅ 已验收（流 8 项 + 预览 12 项 ALL_PASS） |
| C6 | TUI 差异收敛（后台任务/子代理/权限模式）与文档四列表 | ✅ 已验收（目录浏览/@ 引用/能力探测/技能/子代理/计划模式与权限投影；`c6-e2e.mjs` 真机 30 项 ALL_PASS） |
| C7 | 全量 E2E 回归 + 完成审计 | ✅ 已验收（deliverables 27 项、轨迹 16 项、目标条 28 项、C4 19 项、C5 8 项、C6 30 项、终端面板 11 项、Core 单测 364+/364+） |
| C8 | 提问卡片：`user-questions/request` waterfall + 批量回帖 + 结算 cancel 帧 | ✅ 已验收（单测 10 + `dsh-webui-questions.mjs` 真机 23 项 ALL_PASS） |
| C9 | 会话反馈：`sessionFeedback/record` request 信封 + `feedback/record` 日志事件 + 契约错误联合体 | ✅ 已验收（单测 4 + `dsh-webui-session-feedback.mjs` 真机 18 项 ALL_PASS） |
| C10 | 模型页与凭据：供应商虚拟命名空间 `settings/describe|mutate`（`providers_llm/config.json` + `models.json`）、`credentials/{describe,set,unset}` 共用 `auth.json`、`llm/discoverModels`、`@` 会话引用 `sessionReferenceResolver/candidates` | ✅ 已验收（单测 12 + `dsh-webui-models-settings.mjs` 27 项、`dsh-webui-credentials-references.mjs` 23 项真机 ALL_PASS） |
| C11 | 未接线表面诚实化：`account/*` 部署级引导、`pluginManager/*` 空态/拒绝、`pluginRegistryProbe/fastest` null、`officeToPdf/*` 服务不可用引导、`dynamicCordisRunner/{stopFromPanel,undefineFromPanel}` plugin-missing | ✅ 已验收（单测 6 + `dsh-webui-unsupported.mjs` 13 项真机 ALL_PASS，插件页显示不可管理空态） |
| C12 | 通用设置页「打开配置文件」：`settings/describe.hasDocument=true` + `settings/openSettingsDocument` 落盘后交系统默认程序 | ✅ 已验收（单测 2 + `dsh-webui-settings-document.mjs` 8 项真机 ALL_PASS） |
| C13 | 窗口缩放/最大化后 WebView2 控制器尺寸跟随（缩窗后双击最大化露出未绘制黑区） | ✅ 已验收（单测 8 + 真机探针 `1440×900 → 884×601 → 1920×1017 → 884×601`，CDP 视口同步无黑区） |
| C14 | 跨屏 DPI 切换时控制器物理尺寸跟随（`RenderScaling` 变化） | ⏳ 待真机验证：本机仅单显示器 1920×1080@100%，无 DPI 差异目标；合成 `WM_DPICHANGED` 会制造与系统不一致的假 DPI 状态，不作为缺陷证据 |

## 四、验收四列表（滚动更新）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `DshConPty`（Windows ConPTY，CreatePseudoConsole + 原始管道 I/O） | `DshTerminalService` 创建/写入/缩放/关闭真实 shell | ✅ | ✅ 单测 9 例 + `dsh-webui-terminal.mjs` ALL_PASS（真实 pwsh echo） |
| `DshTerminalService.Follow/Retain` + `DshWebHost` terminal 流 | 侧栏终端面板快照/输出/state/retained 帧 | ✅ | ✅ `dsh-webui-terminal-panel.mjs` ALL_PASS（xterm 挂载、controller、界面 echo） |
| `DshJobService` 投影 + `DshWebHost` job 路由 | 后台任务面板 `rows/output/status/kill` | ✅ | ✅ 单测 5 例 + `dsh-webui-jobs.mjs` ALL_PASS（rows、job/not-found） |
| `DshWebAssets` 缓存完整性校验 + `package_dsh_web.py` 分片校验 | 侧栏 terminal/pdf/excel 懒加载分片 | ✅ | ✅ 终端面板真机 ALL_PASS + `dsh-webui-c5-preview.mjs` 12 项 ALL_PASS（PDF 3 页文本层、Excel 画布 23,462 像素） |
| `DshWorkspaceFilesService.ResolveRootedAsync`（根内绝对/相对路径，弹栈防穿越） | 文件树 `workspaceFiles/*`（前端发 state.root 绝对路径） | ✅ | ✅ 单测 5 例（绝对根/子目录/越界/穿越）+ `c5-stream-e2e.mjs` 文件树 3 文件 |
| `DshSessionFeedbackRecordArgs/Result/Value` + `DshFeedbackRecordData`（`DshJsonContext` 注册）+ `DshRpcDispatcher.SessionFeedbackRecord` | 会话头「更多操作」→「反馈」弹窗提交 → `POST /api/sessionFeedback/record` → 会话日志 `feedback/record` | ✅ | ✅ 单测 4 例（trim/空白省略/未知会话/分类白名单）+ `dsh-webui-session-feedback.mjs` 真机 18 项 ALL_PASS（真实弹窗提交、`{recorded:true}` 回执、日志 trim、无助手事件） |
| `DshWorkspaceFilesService.ReadBytesAsync` + `DshWebHost.BuildMultipartPayload`（metadata 信封 + bytes-0 Blob） | 文档预览 / 图片预览读取 `Uint8Array` | ✅ | ✅ 单测 2 例（空占位 + multipart 布局）+ 真机 PDF/Excel 渲染 ALL_PASS |
| `DshWebHost` `workspaceFiles/changes` 流（ready/change/absent/error，500ms 轮询快照对比） | 文件树变更刷新、重命名/删除感知 | ✅ | ✅ `c5-stream-e2e.mjs` 8 项 ALL_PASS（写入/追加版本变化、移出→absent、非法会话引导） |
| `DshWebHost.StreamError` 补齐 details 对象 | 所有流式端点错误帧（job/terminal/workspaceFiles） | ✅ | ✅ `dsh-webui-jobs.mjs` 收到 `job/not-found`（修复前 WS 1006） |
| `DshSessionStore.SearchAsync`（字面量、仅 user/assistant、20 条上限、240 字片段、第 21 条置 hasMore） | 会话搜索面板 `session/search` | ✅ | ✅ 单测（命中/上限/片段窗口）+ `c4-e2e.mjs` 真实会话命中 2 条 |
| `DshSettingsStore.Replace/Update`（整段替换 / 顶层补丁、null 删除、revision 乐观并发） | 设置中心各命名空间写路径 | ✅ | ✅ 单测（替换/补丁/冲突）+ `c4-e2e.mjs` revision=1→2、旧 revision 冲突 |
| `DshWorkspacePathService`（Explorer 打开/选中，绝对路径 + 存在性校验） | `session/{openWorkspacePath,workspacePathApplications}` | ✅ | ✅ 单测（仅 Explorer/空表/引导错误）+ `c4-e2e.mjs` 真实 cwd 返回 explorer、相对路径被拒 |
| `DshMessageFeedbackStore` + dispatcher 写路径（opaque string version、4096 字节备注、target 校验） | 消息反馈 `messageFeedback/{list,put,delete}` | ✅ | ✅ 单测 4 例 + `c4-e2e.mjs` 新建/冲突/备注规则/删除共 7 项 |
| `DshAgentPreset*` DTO + 路由（standard 预设、默认项读设置） | 代理预设面板 `agentPresets/{list,read,select}` | ✅ | ✅ 单测 + `c4-e2e.mjs` list/read/select 三方法 |
| `DshDirectoryBrowser`（只列子目录、隐藏项、面包屑、1000 条截断、单段名新建） | 目录选择器 `directoryPicker/{list,createDirectory}` | ✅ | ✅ 单测 10 例（home/截断/相对路径/重复名/多段名） |
| `DshFileReferenceService`（会话 cwd 索引、5s 缓存、排除目录、模糊排序、20 条上限） | 输入框 @ 文件引用 `fileReferences/list` | ✅ | ✅ 单测 6 例（根目录/子目录/模糊/隐藏/排除/越界/未知会话）+ RPC 2 例 |
| `DshPathText`（平台无关 /、C:/、UNC 路径解析与根内校验） | 目录浏览与文件引用索引的路径归一 | ✅ | ✅ 间接覆盖：InMemoryFileSystem 全路径用例 23 例 |
| `DshWorkspacePathService.IsSupported` | `session/canOpenWorkspacePath` 能力探测 | ✅ | ✅ RPC 单测（Windows=true 与平台断言一致） |
| `JccChatSession.GetAvailableSkillsAsync` + `SkillSummary`（引擎 `ISkillService` → DTO） | 技能面板 `skills/list` | ✅ | ✅ 单测 6 例（RPC 4 + 映射 2） |
| `DshRpcDispatcher.SubagentPrompt`（DshRequestEnvelope → `ForwardInputToSubAgentAsync`，requestId 回显 messageId） | 子代理卡片发送框 `subagents/prompt` | ✅ | ✅ 单测 5 例（文本拼接/图片拒绝/未知子代理/非法 mode/回显） |
| `DshRpcDispatcher.SubagentInterruptByParent`（扁平 childSessionId → `InterruptSubAgentAsync`） | 子代理卡片停止按钮 `subagents/interruptByParent` | ✅ | ✅ 单测 3 例（受理/未知/缺参） |
| `DshSessionStreamHub.PublishToOpenSessionsAsync` + `LastPlanMode` 去重 | 计划模式指示器 `plan/mode` 事件（轮次起点 + 权限预设切换） | ✅ | ✅ 单测 4 例（轮次前投影/两次只发一次/离开发 false/多会话广播） |
| `DshSessionControlHub`（订阅者快照广播、单订阅异常隔离、开场缓冲补发） | `session/control` 实时投影帧 | ✅ | ✅ 单测 2 例（投递/解绑、异常订阅者不阻塞）+ `c6-e2e.mjs` 控制帧 plan/permissions |
| `DshHostServices.ApplyPermissionPresetAsync`（校验 → 引擎 → 会话投影 → 静默落设置 → plan/mode + 控制帧） | 权限选择器 `settings/mutate`、计划 chip `/plan off`、引擎 attach 补应用 | ✅ | ✅ 单测 5 例（plan 生效/非法拒绝/未就绪/off/on）+ `c6-e2e.mjs` seq 水位与 UI chip 同步 |
| `DshSessionStore` plan/permissions 投影 + `DshRpcDispatcher` `/plan` 拦截 | `plan` chip、`permissions` 选择器、`/plan on|off|status` | ✅ | ✅ 单测 3 例 + `c6-e2e.mjs` 30 项（技能/子代理引导/控制帧/UI 截图） |
| `DshTurnChangeCollector`（ToolCallStart 抓文件坐标 → 轮末读盘算增删行）+ `DshTurnRunner` 取消/失败路径同样收尾 | deliverables 改动卡片数据源 `workspace/changes` 事件 | ✅ | ✅ 单测 5 例 + `c7-changes-e2e.mjs` 真实 write：`changes=59 < turnEnd=61`、摘要 `+2 -0` |
| `DshWebHost` deliverables 端点（`api/changes.{summary,diff,open}`、`api/present.{host,open}`） | 前端改动卡片/右侧审阅/原生打开 | ✅ | ✅ `c7-changes-e2e.mjs` 27 项（200/404/405/422、真实 diff 行、explorer 打开） |
| `DshSessionStreamHub.PublishAsync(onAllocated)`（摘要先于事件可见） | 前端收到 `workspace/changes` 后按 seq 立即拉摘要 | ✅ | ✅ 真机断言摘要可读（修复前会永久缓存 missing） |
| `DshSettingsStore` `ui-settings` 命名空间（enabled 默认 true + schema 信封） | deliverables 卡片渲染门槛 `developerTools` | ✅ | ✅ 单测 1 例 + `c7-changes-e2e.mjs` 卡片标题/审阅渲染 |
| `DshConPty.BuildStartupInfo`（`STARTF_USESTDHANDLES` + 三个 INVALID_HANDLE_VALUE） | ConPTY 子进程标准句柄不继承宿主重定向 | ✅ | ✅ 单测 1 例 + 真机终端面板 11 项（命令真实执行、cwd 正确） |
| `DshTerminalInfo.Error` 空值省略（`JsonIgnore(WhenWritingNull)`） | 侧栏终端错误条（前端以 undefined 判无错） | ✅ | ✅ 单测 1 例 + 真机断言页面无“终端错误：null” |
| `DshTerminalService.ResolveCwdAsync` 正斜杠归一 | `terminal/create` 会话 cwd 交给 CreateProcess | ✅ | ✅ 单测 1 例 + 真机 `PS D:\...\c7-ws>` |
| `pluginInventory/list` 空清单快照（`managementAvailable=false`） | 插件管理页空态 | ✅ | ✅ 单测 1 例 |
| `DshSessionStore.ToGoalFace` + `DshHostServices.PublishGoalProjectionAsync`（无目标 JSON `null`；有目标 `{goal,roundsStarted,createdAt,updatedAt}`；写路径后推 `session/control` goal 整值帧） | 前端 GoalDock `useProjection("goal")`、控制帧实时刷新目标条 | ✅ | ✅ 单测 5 例 + `c7-goal-e2e.mjs` 真机 28 项（初始 undefined → 建目标渲染 → 暂停/恢复 → 编辑 → 清除隐藏、基线/帧 seq 水位、两张截图） |
| `DshRpcDispatcher` goals 七方法（agent 作用域：get/create/edit/pause/resume/complete/clear，create/edit 走 `request` 信封，clear 返回裸 `{id,revision}`，get 无目标省略 `value`） | 目标条按钮 `remote.goals.*`、`useGoalActivation` 读 `goals/get.activation` | ✅ | ✅ 单测 5 例（agent wire/undefined/裸 ref）+ 真机 UI 暂停/恢复/编辑/清除全部生效 |
| `WebView2Host.ResolveControllerBounds` + `ArrangeOverride(finalSize)` 直传 | 所有 WebView2 宿主窗口（`DshWebUiWindow`/`WebUiWindow`）的缩放、最大化/还原 | ✅ | ✅ 单测 8 例 + 真机探针（缩小→最大化→还原，`Chrome_WidgetWin_0` 每步等于客户区；CDP `innerWidth/innerHeight` 同步 1920×1017 / 884×601，页面无黑区） |
| `DshUserQuestionBroker`（`$events` 挂接/断连 fail-closed、`user-questions/request` 五键帧、q0..qN 答案映射、结算后 cancel 帧、轮次收尾按会话取消） | 引擎 `ask_user_question` → 前端问题卡片 → `$events/result` → 工具结果 | ✅ | ✅ 单测 10 例 + `dsh-webui-questions.mjs` 真机 23 项 ALL_PASS（真实桩模型 → 卡片 → 提交 → `"是否继续执行？"="继续"`、`"选择要包含的选项？"="A, B"` → `turn/end completed`） |
| `AvaloniaInteractiveService.ShowQuestionsCallback` + `IJccChatSession.AskUserQuestionsCallback` 转发（GUI DI Order=80 单例） | 引擎工具处理器取整批问题一次性投递给前端卡片（而非逐题原生对话框） | ✅ | ✅ 同上真机 E2E：一张卡片渲染 2 问、单选自动翻页、多选 A/B 同时提交 |
| `userQuestions/answer` 诚实 false（宿主无 continued 队列）+ `userQuestions/attachWait` 空流（无 timed wait） | 前端在 continued 状态下才走 RPC 回帖；本轮宿主不产生该状态 | ✅ | ✅ 单测 2 例（route 回 false + 引导文案含 callId/answer、attachWait 空流） |
| `DshProviderSettingsStore`（`llm-deepseek` 虚拟命名空间：`describe` 聚合 `providers_llm/` 投影，`mutate` 按 set/delete 路径写 `config.json`/`models.json`，字段白名单 + 模型按 id 合并保留隐藏字段，revision 对自身写入稳定） | 模型页供应商卡片编辑、自定义设置折叠区、模型清单增删、`settings/mutate` 路由 | ✅ | ✅ 单测 9 例 + `dsh-webui-models-settings.mjs` 真机 27 项 ALL_PASS（卡片/编辑/自定义设置展开/保存/二次保存/发现模型/隐藏字段保留） |
| `SettingsLoader.SaveVendorConfigAsync` + `SaveVendorModelsAsync`；`ConfigLoader.LoadStoredApiKeyAsync` / `RemoveApiKeyFromJccAsync` | 模型页写盘与凭据读写（与 TUI/CLI 共用 `~/.jcc/auth.json`） | ✅ | ✅ 单测（供应商配置合并、模型文件往返、DPAPI 落盘不含明文、损坏 auth.json 拒绝覆盖） |
| `DshCredentialStore` + `DshRpcDispatcher.credentials/*`（`DeriveKeyRef` 路由映射、env 优先、可写判定、设置后热更新引擎） | 模型页 API 密钥输入/清除、模型发现复用密钥 | ✅ | ✅ 单测 6 例（含环境变量遮蔽时拒绝移除且不改盘）+ `dsh-webui-credentials-references.mjs` 真机 ALL_PASS（UI 填密钥→auth.json 加密、describe=stored、unset→false、env 拒绝指名变量） |
| `DshModelDiscoveryService`（`llm/discoverModels`：OpenAI/Anthropic 响应归一、基址版本段自适应、20s 超时、500 条上限、协议鉴权头） | 模型页「发现模型」按钮、供应商模型清单刷新 | ✅ | ✅ 单测（Bearer/x-api-key、`context_length` 映射、错误引导）+ 真机 `discoverModels 复用已存密钥` 命中 `stub-model` |
| `DshSessionStore.ReferenceCandidatesAsync` + `sessionReferenceResolver/candidates` 路由（排除自身、标题/ID/目录匹配、同工作区优先、最多 20 条） | 输入框 `@` 菜单「会话」分组、mention chip | ✅ | ✅ 单测（命中/排除自身/未知会话 context-not-found）+ `dsh-webui-credentials-references.mjs` 真机（fork→rename→候选命中→`@参考` 菜单出现会话） |
| `DshRpcDispatcher.Unrouted`（`account/*` 前缀 → `account/unsupported` 部署级引导） | 账号设置页（仅在 DSH 桌面渲染器挂载；Web 端 `dshDesktop` 缺失不注册） | ✅ | ✅ 单测 1 例 + `dsh-webui-unsupported.mjs` 真机（`dshDesktop` 探测 + getState 错误文案） |
| `DshPluginRegistriesView` / `DshPluginVersionExemptions` + `pluginManager/*` 读写路由（读路径空形状、写路径 `plugin-manager/rejected`）+ `pluginRegistryProbe/fastest` → null | 插件页空态与安装入口（无 profile 时不触发写路径） | ✅ | ✅ 单测 2 例 + 真机 ALL_PASS（插件页显示「本部署没有可管理的 profile，无法安装或启停插件」） |
| `officeToPdf/generation` 稳定令牌 + `officeToPdf/render` → `gateway/service-unavailable`（含 PDF/xlsx 替代方案） | docx/pptx 预览失败提示（前端把该错误码映射为「不可用」） | ✅ | ✅ 单测 1 例 + 真机 ALL_PASS（错误码与文案含 PDF 替代路径） |
| `DshCordisPanelActionResult` + `dynamicCordisRunner/{stopFromPanel,undefineFromPanel}` → `{ok:false,reason:"plugin-missing"}` | Cordis 面板停止/移除按钮（inventory 为空时不渲染行，直接调用也有可读失败） | ✅ | ✅ 单测 1 例 + 真机 ALL_PASS（两个动作均回 plugin-missing，不再 unknown method） |
| `DshSettingsStore.Describe.HasDocument=true` + `OpenDocument()`（`Persist` 后交 `OpenFile` 默认 shell 打开）+ `settings/openSettingsDocument` 路由 + `DshSettingsDocumentOpenValue` | 通用设置页头部「打开配置文件」按钮（`hasDocument` 门槛）→ `/api/settings/openSettingsDocument` | ✅ | ✅ 单测 2 例（落盘后打开/启动失败引导含文件路径）+ `dsh-webui-settings-document.mjs` 真机 8 项 ALL_PASS（describe、文件内容、按钮可见可点、截图） |

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 新建 TASK066 而非复用 TASK065（同名文档已被 TUI 任务占用） -->
<!-- 原因: 避免覆盖既有 TUI 全量对齐文档；GUI 对接范围独立建档便于四列验收 -->
<!-- 替代方案: 追加到 TASK060 —— 会混淆「DSH 宿主」与「TUI 差异」两条主线，不采用 -->
<!-- 验证: 文档创建，覆盖矩阵以启动清单 + remotes schema + 路由表三方交叉核对 -->

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: job/* 流参数按生成器 wire="request" 统一包成 {request:{...}}，terminal/* 保持扁平参数 -->
<!-- 原因: 生成器描述符是协议事实源；此前按扁平解析导致 job/list 一直空、job/follow 报 not-found -->
<!-- 替代方案: 同时兼容两种形态 —— 增加无意义分支，前端只有一个真实调用形态，不采用 -->
<!-- 验证: dsh-webui-jobs.mjs ALL_PASS（rows 首帧 + job/not-found 错误帧） -->

<!-- 🤖 Auto Decision: 2026-10-22 (C4) -->
<!-- 决策: settings 只实现 replace/update（不造 set/unset）；session/search 走转录消息而不是标题；openWorkspacePath 只公布 explorer；反馈 version 用不透明字符串 -->
<!-- 原因: dsh-api-remotes/client.js 是协议事实源——settings 只有 describe/mutate/replace/update，replace 整段替换、update 顶层补丁；search 参考实现只命中消息正文、20 条上限、240 字片段；随机程序执行会把前端参数变命令注入面；反馈契约的 version 是 string 且 ifVersion 参与乐观并发 -->
<!-- 替代方案: 兼容 set/unset 或暴露 shell/open 任意程序 —— 前者无真实调用方、后者越过安全边界，均不采用 -->
<!-- 验证: 单测 13 例（含修复新建反馈 Clone(null) NRE）+ NativeGui.Host 真机 c4-e2e.mjs 19 项 ALL_PASS；GUI core 全量 290/290 -->

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 流式错误帧 details 显式序列化为空对象；资产缓存按「meta.json + 三个懒加载分片」判定完整 -->
<!-- 原因: JsonElement 默认 Undefined 序列化抛 InvalidOperationException 导致 WS 1006；旧缓存只有 meta 会让新分片永远不落盘 -->
<!-- 替代方案: 前端忽略 details / 每次启动重解包全量资产 —— 前者破坏协议形状，后者浪费 IO，不采用 -->
<!-- 验证: jobs E2E 错误帧恢复；终端面板真机 xterm 挂载并回显 echo UI_TERMINAL_OK（ALL_PASS） -->

<!-- 🤖 Auto Decision: 2026-10-22 (C5) -->
<!-- 决策: readBytes 改为 multipart/form-data（metadata JSON + bytes-0 Blob），value.data 置 null 占位；workspaceFiles/* 同时接受根内绝对路径 -->
<!-- 原因: DSH 前端 schema 断言 data 为 Uint8Array（base64 字符串报 invalid_type path ["data"]）；文件树发送 state.root 绝对路径，旧实现按相对解析报「目录不存在」 -->
<!-- 替代方案: 前端改解析 base64 / 仅接受相对路径 —— 前端是黑盒资产不可改，且协议事实源在 dsh-client-connection 的 parseBinaryResponse，不采用 -->
<!-- 验证: 单测 12 例（含 multipart 布局与 Content-Length）；c5-stream-e2e 8 项 + dsh-webui-c5-preview 12 项真机 ALL_PASS（PDF 3 页、Excel 画布 23,462 像素） -->
<!-- 坑: MemoryStream 写完后指针在末尾，CopyToAsync 复制 0 字节导致 Content-Length 不符、客户端无限等待；改为 BuildMultipartPayload 一次成型返回 byte[] -->

<!-- 🤖 Auto Decision: 2026-10-22 (C6) -->
<!-- 决策: 目录浏览只回子目录并用平台无关 DshPathText 归一（/、C:/、UNC）；@ 引用按会话 cwd 建 5s 缓存索引；initializeDefaultModel 诚实 no-op；canOpenWorkspacePath 返回桌面宿主能力 -->
<!-- 原因: dsh-host-directory-picker-browse 只列目录、隐藏项按点前缀、1000 条截断、新建名必须单段；dsh-file-reference-local 裸词模糊排序、空/斜杠查询列当前目录、排除构建目录、20 条上限；本地宿主无 deepseek 账号默认模型概念 --> 
<!-- 替代方案: 复用 System.IO.Path（Windows 测试难覆盖 / 路径）/ 每次请求全量扫描索引（大仓库卡顿）/ 伪造默认模型初始化 —— 均不采用 -->
<!-- 验证: 单测 23 例全绿（目录浏览 10 + 文件引用 6 + RPC 7），NativeGui.Core 全量 319/319 -->

<!-- 🤖 Auto Decision: 2026-10-23 (C6 续) -->
<!-- 决策: skills/list 直读引擎 ISkillService；subagents/prompt 文本块按换行拼接、messageId 回显 requestId；image 块显式拒绝；plan/mode 在轮次起点按引擎权限模式投影并去重，权限预设切换时向打开会话广播 -->
<!-- 原因: 引擎 SkillDefinition 只有名称/描述/DisableModelInvocation/SourcePath，无 WhenToUse（DTO 可空省略）；ForwardUserInputToAgentAsync 是纯文本通道，直接静默丢弃图片会让前端以为已送达；DSH 计划面板按会话折叠 plan/mode 持久事件，而引擎权限模式是应用级状态，需要投影 + 取最近一次事件去重（无记录视为 false） -->
<!-- 替代方案: 伪造技能数据 / 图片转 base64 塞进文本 / 每轮无条件写 plan/mode —— 前者欺骗 UI，中者子代理无法解析，后者污染日志且破坏既有事件序列断言，均不采用 -->
<!-- 验证: 单测 15 例全绿（RPC 11 + 计划模式 4）+ 计划模式广播 1 例 + Host 技能映射 2 例；NativeGui.Core 全量 334/334、Host.Tests 技能 2/2 -->

<!-- 🤖 Auto Decision: 2026-10-23 (C7 计划/权限投影) -->
<!-- 决策: 权限预设作为应用级状态投影到每个会话；plan{active,pending=false} / permissions{currentValue} 由宿主计算并经 session/control 推实时帧；/plan on|off|status 由宿主拦截并映射到引擎 PermissionMode，静默落 permission/defaultPreset -->
<!-- 原因: 前端 ui-plan 读 useProjection("plan")（pending 时 chip 显示规则相反）、ui-permission-presets 读 permissions.currentValue；引擎 PlanCommand 的 PlanModeManager 与 PermissionMode 是两套状态，直接委托会让 chip 与引擎权限不一致；settings/mutate 回环再写 setting 会递增 revision 造成前端 expectedRevision 冲突 -->
<!-- 替代方案: 前端改读 plan/mode 持久事件（chip 只在轮次后刷新，交互延迟）/ 委托引擎 PlanCommand（chip 不反映引擎权限模式）/ 每次应用都重写设置（revision 双跳），均不采用 -->
<!-- 验证: 单测 8 例全绿 + NativeGui.Core 全量 342/342 + NativeGui.Host 0 警告；c6-e2e.mjs 真机 30 项 ALL_PASS（19 条技能、子代理引导 3 项、控制帧 seq>基线水位、计划 chip 出现/点击/权限选择器 Plan→Auto、截图 dsh-webui-c6-plan-on/off.png） -->

<!-- 🤖 Auto Decision: 2026-10-24 (C7 deliverables/轨迹) -->
<!-- 决策: 改动摘要在轮末从盘上重算（ToolCallStart 记录坐标），事件序号分配后先落摘要再推送；摘要/对比/原生打开走独立 HTTP api/changes.*，presented 文件走 api/present.* -->
<!-- 原因: 前端收到 workspace/changes 后立即按 seq 拉摘要，若摘要晚于事件可见会命中 404 并缓存 missing；引擎不提供产物 diff 能力，只能在宿主侧按前后快照计算 -->
<!-- 替代方案: 事件里内联 diff 文本 / 前端等待轮末再拉 —— 前者让事件体膨胀且不利于重放，后者增加时序窗口，均不采用 -->
<!-- 验证: c7-changes-e2e.mjs 27 项 + c7-trajectory-e2e.mjs 16 项真机 ALL_PASS（真实 write 落盘、+2/-0 行、审阅渲染、轨迹 5 轮无加载态） -->

<!-- 🤖 Auto Decision: 2026-10-24 (ConPTY 标准句柄) -->
<!-- 决策: ConPTY 子进程启动信息显式设置 STARTF_USESTDHANDLES + hStdInput/hStdOutput/hStdError = INVALID_HANDLE_VALUE，bInheritHandles 保持 false -->
<!-- 原因: 宿主 stdout 被重定向到日志时（GUI 宿主常见启动方式），Windows 会把宿主的标准句柄复制给子进程，盖过伪控制台：提示符写进宿主日志、xterm 空白、输入只回显不执行。实验矩阵（cmd / PowerShell 5.1 / pwsh 7）证明仅改 bInheritHandles 无效，显式置空三个句柄后三条 shell 全部把输出送进 PTY -->
<!-- 替代方案: 启动宿主时不重定向 stdout（调用方脆弱）/ 直接把 PTY 管道句柄塞进 STARTF_USESTDHANDLES（绕过 conhost 行编辑，破坏 ANSI 渲染），均不采用 -->
<!-- 验证: 单测 1 例 + 真机终端面板 11 项 ALL_PASS（echo 命令与结果、cwd、无错误条） -->

<!-- 🤖 Auto Decision: 2026-10-24 (终端错误条) -->
<!-- 决策: DshTerminalInfo.Error 为 null 时不序列化字段 -->
<!-- 原因: 前端 client.terminal.js 以 error !== undefined 判断是否渲染错误条，JSON null 会被渲染成「终端错误：null」 -->
<!-- 替代方案: 改前端片段（打包资产黑盒，升级会被覆盖）/ 全局改 DshJsonContext 的 WhenWritingNull（影响所有协议 DTO 形状），均不采用 -->
<!-- 验证: 单测 1 例 + 真机断言无「终端错误」文本 -->

<!-- 🤖 Auto Decision: 2026-10-24 (目标条投影与 goals wire) -->
<!-- 决策: 目标投影面「无目标」输出 JSON null（不是 {goal:null}），有目标输出 {goal,roundsStarted,createdAt,updatedAt} 且内层不含 activation；goals 七方法按 agent 作用域（agentId），create/edit 用 request 信封，clear 返回裸 {id,revision}，get 无目标省略 value（undefined） -->
<!-- 原因: 前端 GoalDock 的 activeRef 直接读 projection.goal.phase，{goal:null} 会让整个 conversation.input.dock 槽位抛 TypeError 崩溃，目标条永不渲染；dsh-api-remotes 描述符是 Web 调用事实源——goals 全部 context:"agent" 走 agentId，edit 第三参数名为 request（不是 typert.remote-client 的 changes），clear 结果是裸 GoalRef，get 结果是 GoalView|undefined（value:null 会被 strict 解码拒绝，activation 读不到就不显示暂停/恢复按钮） -->
<!-- 替代方案: 前端改判空（打包资产黑盒，升级覆盖）/ host 同时兼容 sessionId 与 agentId（前端只有一个真实调用形态，徒增分支），均不采用 -->
<!-- 验证: 单测 5 例全绿（投影 null 序列化、控制帧、agent wire、undefined 信封、裸 ref）；c7-goal-e2e.mjs 真机 28 项 ALL_PASS（条渲染→暂停/恢复/编辑/清除、UI 截图、清除后投影 null）；Host Debug 0 警告 0 错误 -->

<!-- 🤖 Auto Decision: 2026-10-24 (真机 E2E 启动方式) -->
<!-- 决策: 真机 E2E 必须用 WMI 脱离 Agent 作业启动 NativeGui.Host，且启动脚本注入 STUB_API_KEY -->
<!-- 原因: 从 Agent Bash 作业内启动的进程创建 ConPTY 时子进程立即退出；无 stub key 时引擎不进 attached，skills/list 等依赖引擎的断言超时 -->
<!-- 替代方案: 在作业内直接启动 / 用 mock 引擎 —— 前者无法复现真实终端，后者违反 ADR 0080 非 mock 验收，均不采用 -->
<!-- 验证: 终端面板 11 项、c6 30 项、c7 27+16 项连续 ALL_PASS -->

<!-- 🤖 Auto Decision: 2026-10-24 (提问卡片 user-questions) -->
<!-- 决策: 引擎 ask_user_question 整批问题 → 一张前端卡片；宿主阻塞式问答（不回 wait 字段）；userQuestions/answer 诚实 false；attachWait 返回空流；每次结算/取消都补发 {type:"cancel",eventId} -->
<!-- 原因: 前端 QuestionComposer 在 waterfall 通道下一次性渲染全部问题并批量回帖，逐题原生对话框会拆成多张卡；timed wait/continued 队列是 DSH 宿主侧能力，本地宿主没有对应状态，伪造会让前端等待永不到来的倒计时或继续队列；不补 cancel 帧前端会一直等待该 waterfall -->
<!-- 替代方案: 逐题 ShowQuestionsAsync（多次弹卡，打断回答节奏）/ 回 wait:60000 制造假倒计时 / answer 假装受理（前端会进入 continued 但宿主无队列）—— 均不采用 -->
<!-- 验证: 单测 10 例全绿（五键帧/答案映射/cancel/断连/换会话/旧客户端幂等/answer 诚实 false）；dsh-webui-questions.mjs 真机 23 项 ALL_PASS（桩模型工具调用 → 单选+多选卡片 → 提交 → 工具结果 → turn/end completed → 无 unimplemented/error 帧） -->

<!-- 🤖 Auto Decision: 2026-10-24 (会话反馈 sessionFeedback/record) -->
<!-- 决策: sessionFeedback/record 只把反馈写进会话日志事件 feedback/record（trim 文本，空白省略，可选分类），成功返回 {ok:true,value:{recorded:true}}，未知会话返回契约错误联合体 {ok:false,error:{code:"session-not-found",sessionId}}；分类按七词汇白名单校验，非法值走 gateway/bad-request 并列出全部合法值 -->
<!-- 原因: dsh-command-feedback 的语义是「记录本次会话级反馈」而非发起模型轮次——若复用消息反馈落盘或触发 prompt，前端会对用户的一次吐槽发起请求；未知会话必须走 result 联合体（strict 解码只接受 value 或 error 二选一），网关失败会让前端弹通用错误而不是提示会话不存在；分类是前端 chip 的固定词汇，白名单能把协议漂移暴露成可读引导 -->
<!-- 替代方案: 复用 messageFeedback/put（需要消息 ID，会话级反馈没有目标消息）/ 静默忽略未知会话（前端 strict 解码失败，toast 只能显示通用错误）—— 均不采用 -->
<!-- 验证: 单测 4 例全绿；dsh-webui-session-feedback.mjs 真机 18 项 ALL_PASS（真实宿主弹窗：选「稳定性和速度」+填写详情→提交→前端「感谢你的反馈」→/api 返回 {recorded:true}→日志 text 已 trim、category=service-stability、surfaceOp 缺省、之后无新增 assistant/message）；Host Debug 0 警告 0 错误 -->

<!-- 🤖 Auto Decision: 2026-10-25 (模型页供应商虚拟命名空间) -->
<!-- 决策: llm-deepseek 等供应商命名空间由宿主从 providers_llm/ 文件投影，settings/mutate 只接受 baseURL/apiKeyEnv/model/models 字段并直写 config.json + models.json；该虚拟命名空间 revision 恒为 0，自身写入不推进版本 -->
<!-- 原因: 引擎没有供应商命名空间的独立文档，值完全来自配置文件；前端模型页编辑器在挂载时快照 revision，保存成功后不会把 mutate 应答折回 describe 镜像（只有设置表单的 mirror.acceptView 会），推进版本会让第二次保存直接 settings/conflict；实测前端确实发送打开时的旧 revision -->
<!-- 替代方案: 写后推进 revision（前端二次保存必冲突）/ 让前端保存后刷新镜像（打包资产黑盒，升级覆盖）/ 省略 revision（strict 解码要求 number，缺失即整页加载失败），均不采用 -->
<!-- 验证: 单测 9 例（连续两次保存均成功、显式错误 revision 仍冲突、模型隐藏字段保留）；dsh-webui-models-settings.mjs 真机 27 项 ALL_PASS（两次保存后 models.json 均更新、发现模型走真实桩） -->

<!-- 🤖 Auto Decision: 2026-10-25 (凭据移除的环境变量优先级) -->
<!-- 决策: credentials/unset 先看环境变量：变量非空即诚实拒绝并指名变量，不再尝试删除 auth.json 条目；只有无环境变量时才删除已存密钥 -->
<!-- 原因: 生效密钥按 env 优先解析；若存在已存密钥时先删盘并返回成功，用户以为密钥没了但请求仍用环境变量密钥，前端 describe 也仍显示 configured=true——删除给出的是错误的安全感；E2E 真实复现该路径（env + stored 并存时 unset 返回 ok 但 source 仍是 env） -->
<!-- 替代方案: 删盘后仍返回成功但提示 env 仍生效（状态已变而结果失败/成功语义混乱）/ 只删盘不提示（用户无法理解为何仍可用），均不采用 -->
<!-- 验证: 红测试先失败后转绿（DshCredentialStoreTests 环境变量遮蔽用例）；dsh-webui-credentials-references.mjs 真机 ALL_PASS（unset STUB_API_KEY 返回 credentials/rejected 且文案含变量名） -->

<!-- 🤖 Auto Decision: 2026-10-25 (模型页 E2E 前置：隔离 home + 空 DEEPSEEK_API_KEY) -->
<!-- 决策: 模型页/凭据真机 E2E 用 JCC_APP_DATA_FOLDER 指向 .xxx/models-home 的独立 home，并以空 DEEPSEEK_API_KEY 启动宿主（仅保留 STUB_API_KEY） -->
<!-- 原因: 共享真实 home 会污染用户 auth.json，且开发机环境里的 DEEPSEEK_API_KEY 会遮蔽已存密钥，使「set→describe=stored」这类断言无法稳定复现；空值让 describe 按 stored 路径回答，同时保留 STUB_API_KEY 供引擎走本地桩 -->
<!-- 替代方案: 直接写真实 home（污染用户凭据）/ 在断言里容忍 env（无法验证落盘链路），均不采用 -->
<!-- 验证: dsh-webui-credentials-references.mjs 23 项 ALL_PASS（UI 保存→auth.json 仅密文、RPC unset→configured=false、env 移除被拒） -->

<!-- 🤖 Auto Decision: 2026-10-25 (未接线表面诚实化：账号/插件/Office/Cordis) -->
<!-- 决策: account/* 走前缀兜底返回 account/unsupported（账号 UI 只在 DSH 桌面渲染器注册，Web 端 dshDesktop 缺失）；pluginManager 读路径回空清单/空注册表/空豁免 + fastest=null，写路径回 plugin-manager/rejected；officeToPdf/generation 回稳定字符串、render 回 gateway/service-unavailable；Cordis 面板动作回严格 schema 的 {ok:false,reason:"plugin-missing"} -->
<!-- 原因: 前端 strict 解码要求形状正确——直接 unknown method 只会显示「网络问题/未知方法」，用户无法判断是本部署不支持还是临时故障；插件页先读 pluginInventory，managementAvailable=false 时不会进入写路径，所以读路径给空形状即可呈现空态；docx/pptx 预览依赖 Office→PDF，本地没有转换进程，诚实回不可用并给出 PDF/xlsx 替代路径；Cordis inventory 为空时面板不渲染行，但直接调用仍需可读结果 -->
<!-- 替代方案: 全部返回 unimplemented（前端 toast 只有技术错误，用户不知道下一步）/ 假装安装成功（前端进入无实际插件的假状态）/ 引入 Office 转换依赖（体积与运行时成本远超预览需求），均不采用 -->
<!-- 验证: 单测 6 例全绿（空形状、写拒绝、generation 字符串、render 错误码、plugin-missing、account 前缀）；dsh-webui-unsupported.mjs 真机 13 项 ALL_PASS（真实宿主 /api 调用 + 插件页空态文案 + dshDesktop 探测）；Host Debug 0 警告 0 错误 -->

<!-- 🤖 Auto Decision: 2026-10-25 (通用设置页打开配置文件) -->
<!-- 决策: settings/describe.hasDocument 恒为 true（本地宿主始终持有 dsh-settings.json）；settings/openSettingsDocument 先 Persist 再交给可替换的 OpenFile 默认 shell 打开，成功回 {opened:true}，启动失败回 settings/rejected 并带文件路径 -->
<!-- 原因: 前端 settings-general 的「打开配置文件」入口完全由 hasDocument 决定，恒 false 会让该动作永久消失（不是不可用提示，而是根本不渲染）；文件只在首次 mutate 时才落盘，直接打开可能命中不存在的文件，所以先同步落盘；OpenFile 作为可替换委托让单测能验证打开路径而不真的弹出编辑器 -->
<!-- 替代方案: 保持 hasDocument=false（入口不可见，用户只能去别处找配置）/ 直接 Process.Start 不落盘（首次打开可能失败）/ 真机 E2E 点击打开（会弹出真实编辑器污染测试机），均不采用 -->
<!-- 验证: 红测试先失败（hasDocument=false / 路由未注册）→ 绿测试 2 例；NativeGui.Core 全量 409/409；Host Debug 0 警告 0 错误；dsh-webui-settings-document.mjs 真机 8 项 ALL_PASS（隔离 home 下文件真实落盘、按钮可见可点）；dsh-webui-unsupported.mjs 回归 13 项 ALL_PASS -->

<!-- 🤖 Auto Decision: 2026-10-25 (设置测试目录拆分) -->
<!-- 决策: 把 DshSettingsStore/DshSettingsStorePersistence/DshProviderSettings/DshSettingsDocument 四个测试移到 tests/native_gui.core.tests/web_host_settings/ -->
<!-- 原因: web_host 目录达到 41 个文件，触发 JCC10001（≤40）；同时仓库规范禁止同目录文件与子文件夹混存（JCC10002），因此改为同级纯文件目录而不是嵌套 settings/ -->
<!-- 替代方案: 在 web_host 下建子文件夹（触发 JCC10002）/ 归档旧测试（无删除红线，且这些测试仍然有效），均不采用 -->
<!-- 验证: web_host 37 个文件、web_host_settings 4 个文件，Core 全量 409/409 编译与测试通过 -->

<!-- 🤖 Auto Decision: 2026-10-25 (窗口缩放后 WebView2 控制器尺寸滞后) -->
<!-- 决策: 控制器物理尺寸由 ArrangeOverride 的 finalSize 直接换算（ResolveControllerBounds），不再读 Bounds；换算结果与当前值相同则跳过 COM 写入 -->
<!-- 原因: Avalonia 在 ArrangeOverride 返回后才赋值 Bounds，读 Bounds 会让控制器永远落后一次窗口尺寸变更。真机探针证实：客户区与 AvaloniaDumbWindow 已变成新尺寸，而 Chrome_WidgetWin_0 仍停在上一次尺寸（缩窗 916x405→884x601 时控制器还是 916x405；最大化到 1920x1017 时控制器还是 884x601），空白区域因此从未被绘制成黑色 -->
<!-- 替代方案: 监听 TopLevel.SizeChanged 后仍读 Bounds（需要额外维护订阅/解除与 DPI 变化路径，且根源仍是陈旧值）/ 定时轮询窗口尺寸（非确定性且浪费），均不采用 -->
<!-- 验证: 红测试先编译失败（ResolveControllerBounds 不存在）→ 绿测试 8 例；Host 全量 44/44；Host Debug 0 警告 0 错误；真机探针 1440x900 → 884x601 → 1920x1017 → 884x601，Chrome_WidgetWin_0 每步等于客户区；CDP innerWidth/innerHeight 同步为 1920x1017 / 884x601，页面铺满无黑区 -->

<!-- 🤖 Auto Decision: 2026-10-25 (跨屏 DPI 切换暂不预防性改代码) -->
<!-- 决策: C14（RenderScaling 变化时的控制器尺寸跟随）只登记待验证，不先加 ScalingChanged 订阅等防御代码；等具备双屏或可切系统缩放的真机环境后按 ADR 0080 复现再修 -->
<!-- 原因: 本机仅一块 1920x1080@100% 显示器，无法产生真实跨屏 DPI 切换；手工向窗口注入 WM_DPICHANGED(144) 会让 Avalonia 内部按 1.5 缩放而系统仍是 96 DPI，实测出现客户区 1924x1061、控制器 1283x707 的假状态，这是注入消息与系统状态不一致的产物，不能当作产品缺陷证据。finalSize 方案本身已按每次排布的 RenderScaling 换算，真实跨屏时 Windows 会改变物理尺寸并触发排布；没有复现就加订阅属于臆测修复 -->
<!-- 替代方案: 依据合成消息的假状态直接加订阅（违反“无复现不修复”，且可能掩盖真实根因）/ 修改用户系统显示缩放做验证（影响整机显示，需用户明确授权），均暂不采用 -->
<!-- 验证: 未改代码；已记录复现条件与探针观察，待双屏真机按同一步骤（Win32 子窗口矩形 + CDP innerWidth/innerHeight/DPR）验收 -->
