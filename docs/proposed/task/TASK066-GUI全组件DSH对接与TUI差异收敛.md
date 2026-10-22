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
| `dsh-client-ui-sidebar-terminal` + `dsh-api-terminal-controller` | 侧栏终端面板 | `terminal/{environment,list,shells,create,write,resize,rename,close,retain}` + `terminal/follow` 流 | ❌ 全部未实现 | 新增 `dsh-webui-terminal.mjs`（创建→echo→快照→关闭）+ 单测 |
| `dsh-client-ui-jobs` + `dsh-api-job-controller` | 后台任务面板 | `job/list`、`job/follow` 流、`job/kill` | ❌ 全部未实现 | 新增 `dsh-webui-jobs.mjs`（后台代理→列表→跟随→kill）+ 单测 |
| `dsh-client-ui-sidebar-files` + `dsh-api-workspace-files` | 文件树 | `workspaceFiles/{list,read,readBytes,stat}` | ✅ 一元；`workspaceFiles/changes` 流 ❌ | 补 changes 流 + 文件树 E2E |
| `dsh-client-ui-sidebar-documentpreview` | PDF/Excel/图片预览 | 仅静态资产 + `workspaceFiles/readBytes` | ✅ 资产缺懒加载分片 → **已修复打包**（本批） | 真机打开 pdf/xlsx 截图 |
| `dsh-client-ui-approval` | 审批面板 | `$events` waterfall + `$events/result` + 审计事件 | ✅ TASK060 B10/B12 | `dsh-webui-approval.mjs` 48/48 |
| `dsh-client-ui-goal` | 目标条 | `goals/*` 七方法 | ✅ | 既有单测（真机 E2E 待补） |
| `dsh-client-ui-plan` | 计划面板 | 无宿主方法（纯前端 + 会话事件） | ⚠️ 事件未投影 | 以 `session/updateQueue` 同层事件补齐 |
| `dsh-client-ui-subagent` | 子代理卡片 | `subagents/*`（catalog 已由 `DshSubagentCatalogProjector` 投影） | ⚠️ 部分 | 子代理 E2E |
| `dsh-client-ui-commands` / `input-trigger` | 斜杠命令/引用补全 | `commands/list|execute` | ✅ | `dsh-webui-prompt` 回归 |
| `dsh-client-ui-settings*`（general/models/shell/agent-loop/subagent/web-search/account/plugins） | 设置中心 | `settings/{describe,mutate}` | ⚠️ 缺 `set/unset/update/replace` | 设置写路径单测 + E2E |
| `dsh-client-ui-model-selection` | 模型选择 | `session/modelCatalog|selectModel` | ✅ | 既有 E2E |
| `dsh-client-ui-skill` | 技能面板 | `skills/list`（当前空） | ❌ 数据源未接 | 引擎技能清单投影 + 单测 |
| `dsh-client-ui-message-feedback` | 消息反馈 | `messageFeedback/list`（只读空） | ⚠️ 缺写方法 | 反馈写方法 + E2E |
| `dsh-client-ui-directory-picker-native` | 原生目录选择 | `directoryPicker/pick` | ✅ | 工作区 E2E |
| `dsh-client-ui-session` | 会话搜索/标题 | `session/rename` ✅、`session/search` ❌ | ⚠️ | 搜索单测 + E2E |
| `dsh-client-ui-user-questions` | AskUserQuestion 卡片 | `$events` 桥 | ✅ | `dsh-webui-approval` 同层 |
| `dsh-client-ui-trajectory` / `deliverables` / `workflow-run` | 轨迹/产物/工作流 | 会话事件 + 文件服务 | ⚠️ 事件投影不全 | 事件矩阵单测 |
| `dsh-api-workspace-controller` | 工作区路径应用 | `session/{openWorkspacePath,workspacePathApplications}` ❌ | ⚠️ | 方法补齐 + 单测 |
| `dsh-client-ui-agent-preset` | 代理预设 | `agentPresets/{list,read,select}` | ⚠️ 仅 list 桩 | 读/选补齐 |
| `dsh-client-ui-open-in-app` / `shortcuts` / `theme` / `brand` | 外壳能力 | 前端独立 + 主题桥 | ✅ | 截图 |
| `dsh-client-ui-plugin-manager` / `cordis` | 插件管理 | `dynamicCordisRunner/*` | ⚠️ 诚实拒绝 | 保持 fail-closed + 引导文案 |

## 二、TUI ↔ GUI 差异与解决方案

| 能力 | TUI 实现 | GUI/DSH 现状 | 差异 | 解决方案（冲突优先 Claude Code） |
|---|---|---|---|---|
| 后台任务 | `BackgroundTaskPanel` + `GetBackgroundAgentsAsync` | jobs 面板无宿主数据 | GUI 缺整块 | 引擎后台代理投影为 `JobView`；`job/list` 流推 `rows`、`job/follow` 推 `output/status`、`job/kill` 映射停止代理 |
| 子代理 | `SubagentGroup` | 卡片部分可用 | 状态/输出未对齐 | 复用 `DshSubagentCatalogProjector`，补 `subagents/prompt|interruptByParent` |
| 权限模式 | `PermissionMode` 循环（default/auto/plan） | `permissionPresets/*` | GUI 只读 | `settings` 写路径接引擎权限模式 |
| 模型选择 | `ModelPickerDialog` | 已接 | 无 | 维持，补变更事件广播 |
| 斜杠命令 | `CommandPickerEngine` + `SlashCommandCatalog` | `commands/list|execute` | 无 | 维持，补 GUI 分类/隐藏字段 |
| 终端 | TUI 本体即终端 | 侧栏终端 ❌ | GUI 缺 | ConPTY 会话管理器 + 原始输出快照回放 |
| 会话搜索 | TUI 历史检索 | `session/search` ❌ | GUI 缺 | 宿主按标题/正文扫描 journal，返回分页结果 |
| 消息反馈 | 无 | DSH 有反馈 UI | TUI 缺 | **以 DSH/Claude Code 为准**，GUI 保留；TUI 不改 |

## 三、批次计划（红 → 实现 → 编译 → 绿 → 文档 → commit）

| 批次 | 内容 | 状态 |
|---|---|---|
| C1 | 懒加载分片打包修复（terminal/pdf/excel）+ 校验 | ✅ 已完成（待提交） |
| C2 | jobs：`job/list`、`job/follow` 流 + `job/kill`，映射后台代理 | ⬜ |
| C3 | terminal：ConPTY 会话管理器 + 10 方法 + `terminal/follow`/`retain` 流 | ⬜ |
| C4 | 小型缺失方法：`session/search`、`settings/{set,unset,update,replace}`、`agentPresets/{read,select}`、`session/{openWorkspacePath,workspacePathApplications}`、`messageFeedback` 写 | ⬜ |
| C5 | `workspaceFiles/changes` 流 + 文档预览真机验收 | ⬜ |
| C6 | TUI 差异收敛（后台任务/子代理/权限模式）与文档四列表 | ⬜ |
| C7 | 全量 E2E 回归 + 完成审计 | ⬜ |

## 四、验收四列表（滚动更新）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `package_dsh_web.py` 纳入懒加载分片并校验缺失 | `dsh_web.zip` → `require.async` 加载 terminal/pdf/excel | ✅ 281 文件/11.46MB | ⬜ 待真机打开终端面板 |
| — | — | — | — |

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 新建 TASK066 而非复用 TASK065（同名文档已被 TUI 任务占用） -->
<!-- 原因: 避免覆盖既有 TUI 全量对齐文档；GUI 对接范围独立建档便于四列验收 -->
<!-- 替代方案: 追加到 TASK060 —— 会混淆「DSH 宿主」与「TUI 差异」两条主线，不采用 -->
<!-- 验证: 文档创建，覆盖矩阵以启动清单 + remotes schema + 路由表三方交叉核对 -->
