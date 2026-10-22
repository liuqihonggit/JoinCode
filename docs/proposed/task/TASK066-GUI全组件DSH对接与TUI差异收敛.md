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
| `dsh-client-ui-sidebar-terminal` + `dsh-api-terminal-controller` | 侧栏终端面板 | `terminal/{environment,list,shells,create,write,resize,rename,close,retain}` + `terminal/follow` 流 | ✅ ConPTY + 10 方法 + follow/retain | 单测 9 例 + `dsh-webui-terminal.mjs` RPC ALL_PASS + `dsh-webui-terminal-panel.mjs` 界面 ALL_PASS |
| `dsh-client-ui-jobs` + `dsh-api-job-controller` | 后台任务面板 | `job/list`、`job/follow` 流、`job/kill` | ✅ 后台代理投影 + rows/output/status | 单测 5 例 + `dsh-webui-jobs.mjs` ALL_PASS（opened 需存在后台代理时覆盖） |
| `dsh-client-ui-sidebar-files` + `dsh-api-workspace-files` | 文件树 | `workspaceFiles/{list,read,readBytes,stat}` + `workspaceFiles/changes` 流 | ✅ 一元 + ready/change/absent/error 帧；路径支持前端绝对路径（根内校验）；readBytes 走 multipart 二进制 | 单测 11 例 + `c5-stream-e2e.mjs` 8 项 ALL_PASS |
| `dsh-client-ui-sidebar-documentpreview` | PDF/Excel/图片预览 | `workspaceFiles/readBytes`（multipart 字节附件） | ✅ 资产打包 + 缓存完整性校验 + 二进制契约 | `dsh-webui-c5-preview.mjs` 12 项 ALL_PASS（PDF 3 页、Excel 画布渲染） |
| `dsh-client-ui-approval` | 审批面板 | `$events` waterfall + `$events/result` + 审计事件 | ✅ TASK060 B10/B12 | `dsh-webui-approval.mjs` 48/48 |
| `dsh-client-ui-goal` | 目标条 | `goals/*` 七方法 | ✅ | 既有单测（真机 E2E 待补） |
| `dsh-client-ui-plan` | 计划面板 | `plan/mode` 事件 + `session/control` 投影 + `/plan on|off|status` | ✅ 权限预设 → 引擎 `PermissionMode` → `plan{active,pending=false}` / `permissions{currentValue}` 投影；前端 chip 点击走 `commands/execute /plan off` 宿主拦截 | 单测 8 例 + `c6-e2e.mjs` 真机 30 项（chip 出现/点击/选择器同步） |
| `dsh-client-ui-subagent` | 子代理卡片 | `subagents/{prompt,interruptByParent}`（catalog 已由 `DshSubagentCatalogProjector` 投影） | ✅ 文本投递 + 中断（image 显式拒绝并引导） | 单测 8 例 + `c6-e2e.mjs` 真机 3 项引导断言 |
| `dsh-client-ui-commands` / `input-trigger` | 斜杠命令/引用补全 | `commands/list|execute`、`fileReferences/list` | ✅ 斜杠命令 + @ 文件引用补全（索引缓存 5s、排除目录、20 条）+ `/plan` 语义接管 | `dsh-webui-prompt` 回归 + 单测 6 例 + c6 真机 |
| `dsh-client-ui-settings*`（general/models/shell/agent-loop/subagent/web-search/account/plugins） | 设置中心 | `settings/{describe,mutate,replace,update}` | ✅ 四条写/读路径齐全 | 单测（整段替换/顶层补丁/revision 冲突）+ 真机 E2E |
| `dsh-client-ui-model-selection` | 模型选择 | `session/modelCatalog|selectModel` | ✅ | 既有 E2E |
| `dsh-client-ui-skill` | 技能面板 | `skills/list` | ✅ 引擎 `ISkillService` 映射（名称/描述/模型可调用/源路径） | 单测（RPC 4 例 + 映射 2 例）+ `c6-e2e.mjs` 真机 19 条技能 |
| `dsh-client-ui-message-feedback` | 消息反馈 | `messageFeedback/{list,put,delete}` | ✅ 落盘 + 乐观并发 + 契约错误联合体 | 单测（版本冲突/备注规则）+ 真机 E2E |
| `dsh-client-ui-directory-picker-native` | 原生目录选择 | `directoryPicker/{pick,list,createDirectory}` | ✅ 原生选择 + 目录浏览（面包屑/隐藏项/1000 条截断）+ 新建目录 | 单测 13 例 + 真机 E2E |
| `dsh-client-ui-session` | 会话搜索/标题 | `session/rename`、`session/search` | ✅ 字面量搜索 + 20 条上限 + 240 字片段 | 单测（片段/hasMore）+ 真机 E2E |
| `dsh-client-ui-user-questions` | AskUserQuestion 卡片 | `$events` 桥 | ✅ | `dsh-webui-approval` 同层 |
| `dsh-client-ui-trajectory` / `deliverables` / `workflow-run` | 轨迹/产物/工作流 | 会话事件 + 文件服务 | ⚠️ 事件投影不全 | 事件矩阵单测 |
| `dsh-api-workspace-controller` | 工作区路径应用 | `session/{openWorkspacePath,workspacePathApplications,canOpenWorkspacePath,initializeDefaultModel}` | ✅ 仅公布资源管理器处理器；能力探测返回桌面宿主；默认模型初始化按本地设置直通 | 单测（引导文案/能力探测）+ 真机 E2E |
| `dsh-client-ui-agent-preset` | 代理预设 | `agentPresets/{list,read,select}` | ✅ 公布 standard（默认项读 `agent-preset-registry.selectedDefault`） | 单测 + 真机 E2E |
| `dsh-client-ui-open-in-app` / `shortcuts` / `theme` / `brand` | 外壳能力 | 前端独立 + 主题桥 | ✅ | 截图 |
| `dsh-client-ui-plugin-manager` / `cordis` | 插件管理 | `dynamicCordisRunner/*` | ⚠️ 诚实拒绝 | 保持 fail-closed + 引导文案 |

## 二、TUI ↔ GUI 差异与解决方案

| 能力 | TUI 实现 | GUI/DSH 现状 | 差异 | 解决方案（冲突优先 Claude Code） |
|---|---|---|---|---|
| 后台任务 | `BackgroundTaskPanel` + `GetBackgroundAgentsAsync` | jobs 面板无宿主数据 | GUI 缺整块 | 引擎后台代理投影为 `JobView`；`job/list` 流推 `rows`、`job/follow` 推 `output/status`、`job/kill` 映射停止代理 |
| 子代理 | `SubagentGroup` | 卡片 + `subagents/{prompt,interruptByParent}` | 无（文本投递；图片暂不支持） | 投递映射 `ForwardInputToSubAgentAsync`（queue/steer 同路径），中断映射 `InterruptSubAgentAsync`；image 块显式拒绝并引导先落盘再用路径 |
| 权限模式 | `PermissionMode` 循环（default/auto/plan） | `permissionPresets/*` + `settings/mutate` + `session/control` 投影 + `/plan` 命令 | 无 | 写路径 → `engine.SetPermissionModeAsync`；宿主计算 `plan{active,pending=false}` / `permissions{currentValue}` 并推控制帧；`/plan on|off|status` 由宿主拦截（对齐 Claude Code 语义），完成后静默落 `permission/defaultPreset` |
| 模型选择 | `ModelPickerDialog` | 已接 | 无 | 维持，补变更事件广播 |
| 斜杠命令 | `CommandPickerEngine` + `SlashCommandCatalog` | `commands/list|execute` | 无 | 维持，补 GUI 分类/隐藏字段 |
| 终端 | TUI 本体即终端 | 侧栏终端 ❌ | GUI 缺 | ConPTY 会话管理器 + 原始输出快照回放 |
| 会话搜索 | TUI 历史检索 | `session/search` ✅ | 无 | 扫描引擎转录的 user/assistant 消息，字面量命中取 240 字窗口，最多 20 条（第 21 条置 hasMore），与 DSH 参考实现一致 |
| 消息反馈 | 无 | DSH 有反馈 UI | TUI 缺 | **以 DSH/Claude Code 为准**，GUI 保留；TUI 不改 |

## 三、批次计划（红 → 实现 → 编译 → 绿 → 文档 → commit）

| 批次 | 内容 | 状态 |
|---|---|---|
| C1 | 懒加载分片打包修复（terminal/pdf/excel）+ 运行时缓存完整性校验 | ✅ 已验收（终端面板真机 xterm + echo） |
| C2 | jobs：`job/list`、`job/follow` 流 + `job/kill`，映射后台代理 | ✅ 已验收（单测 5 + E2E ALL_PASS） |
| C3 | terminal：ConPTY 会话管理器 + 10 方法 + `terminal/follow`/`retain` 流 | ✅ 已验收（单测 9 + RPC/界面 E2E ALL_PASS） |
| C4 | 小型缺失方法：`session/search`、`settings/{replace,update}`、`agentPresets/{list,read,select}`、`session/{openWorkspacePath,workspacePathApplications}`、`messageFeedback/{list,put,delete}` | ✅ 已验收（单测 13 + `c4-e2e.mjs` 真机 19 项 ALL_PASS） |
| C5 | `workspaceFiles/changes` 流 + 文档预览真机验收 | ✅ 已验收（流 8 项 + 预览 12 项 ALL_PASS） |
| C6 | TUI 差异收敛（后台任务/子代理/权限模式）与文档四列表 | ✅ 已验收（目录浏览/@ 引用/能力探测/技能/子代理/计划模式与权限投影；`c6-e2e.mjs` 真机 30 项 ALL_PASS） |
| C7 | 全量 E2E 回归 + 完成审计 | ⬜ |

## 四、验收四列表（滚动更新）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `DshConPty`（Windows ConPTY，CreatePseudoConsole + 原始管道 I/O） | `DshTerminalService` 创建/写入/缩放/关闭真实 shell | ✅ | ✅ 单测 9 例 + `dsh-webui-terminal.mjs` ALL_PASS（真实 pwsh echo） |
| `DshTerminalService.Follow/Retain` + `DshWebHost` terminal 流 | 侧栏终端面板快照/输出/state/retained 帧 | ✅ | ✅ `dsh-webui-terminal-panel.mjs` ALL_PASS（xterm 挂载、controller、界面 echo） |
| `DshJobService` 投影 + `DshWebHost` job 路由 | 后台任务面板 `rows/output/status/kill` | ✅ | ✅ 单测 5 例 + `dsh-webui-jobs.mjs` ALL_PASS（rows、job/not-found） |
| `DshWebAssets` 缓存完整性校验 + `package_dsh_web.py` 分片校验 | 侧栏 terminal/pdf/excel 懒加载分片 | ✅ | ✅ 终端面板真机 ALL_PASS + `dsh-webui-c5-preview.mjs` 12 项 ALL_PASS（PDF 3 页文本层、Excel 画布 23,462 像素） |
| `DshWorkspaceFilesService.ResolveRootedAsync`（根内绝对/相对路径，弹栈防穿越） | 文件树 `workspaceFiles/*`（前端发 state.root 绝对路径） | ✅ | ✅ 单测 5 例（绝对根/子目录/越界/穿越）+ `c5-stream-e2e.mjs` 文件树 3 文件 |
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
