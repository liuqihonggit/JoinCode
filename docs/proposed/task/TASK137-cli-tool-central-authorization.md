# TASK137 — CLI 工具集中鉴权与参数权限编译期门禁

- 状态：**实施中 — 13 组策略已内嵌；local/delegate/host/slash_call 四入口受 broker 约束；首轮组目录与 tree/find/help 均按 agent 工具域裁剪；slash 参数结构化与 base/launcher 待后续阶段**
- 日期：2026-11-25（2026-11-26 更新实施证据）
- 上游决策：[ADR 0168](../adr/0168-cli-tool-central-authorization-and-parameter-policy.md)
- 关联：[ADR 0157](../../implemented/adr/cli_jcc/0157-tool-exposure-data-driven-node-cli.md)、[ADR 0162](../adr/0162-task-scoped-permission-overlay.md)、[ADR 0163](../adr/0163-ai-permission-judge-and-autonomous-mode.md)

## 目标

保留“首轮提示词薄、工具详情通过 `-h` 按需阅读”的现有多 CLI.exe 架构，同时做到：

1. 每个工具组 exe 内嵌参数权限 YAML。
2. `jcc.exe` 是唯一权限决策中心，CLI exe 不解释角色或权限模式。
3. 不同 subagent 的有效工具域只能收窄，不能由调用方请求放大。
4. Roslyn 分析器在编译期拒绝“源码有参数、YAML 无权限”及其反向漂移。
5. local / delegate / host 三条执行路径都受同一授权边界约束。

## 已完成的前置检查

| 检查项 | 结论 | 证据 |
|---|---|---|
| 首轮提示词是否变薄 | 是。基础工具进入协议 tools，组目录只保留摘要，详情由 `-h` 展开 | `modules/cognition/prompts/sections/tools/ToolsSection.cs:29-107`、`resources/tools/base.yaml:22-191`、`resources/tools/cli_groups.yaml:7-85` |
| 工具组入口 | 13 个组共用 `CliNodeRunner`；local 直接调用 handler，delegate 直接启动另一 exe，host 转发 jcc | `modules/abstractions/abstractions_primitives_tools/src/cli_nodes/CliNodeRunner.cs:114-161,210-270` |
| 启动器是否集中鉴权 | 否。`CliToolCommand` 只做 profile 组门禁、目标解析和进程启动 | `modules/apps/cli/core/commands/core/CliToolCommand.cs:140-168` |
| 权限 YAML 现状 | 无。组 `node.yaml` 只有 id/mode/aliases，组 exe 只内嵌 help.txt | `modules/tools/cli_nodes/groups/file/node.yaml`、`modules/tools/cli_nodes/groups/file/jcc_tool_file.csproj` |
| 子代理权限载体 | 已有 `SubAgentContext` 的 AsyncLocal mode/allowed/denied | `modules/abstractions/abstractions_interfaces/interfaces/agent/SubAgentContext.cs:41-57` |
| 跨进程上下文 | Bash/PowerShell 子进程不会自动携带 AsyncLocal；当前 shell 环境也没有 CLI 授权凭据 | `modules/cognition/hands_shell/system_actuator/instances/BashSystemActuator.cs:195-199`、`modules/cognition/hands_shell/system_actuator/abstractions/SystemActuatorCommandContext.cs:142-148` |
| 角色工具合并风险 | `MergeAllowedTools` 在调用方非空时直接覆盖角色定义，可能放大权限 | `modules/llm/agents_spawn/unified/ContextSetupMiddleware.cs:213-226` |
| 参数声明来源 | `[CliOption]`、`[McpToolParameter]`、`[ChatCommandArg]`，以及 local handler 对参数字典的字面量读取 | `modules/apps/cli/core/services/CliArg.cs`、`modules/abstractions/abstractions_primitives_attributes/src/McpToolParameterAttribute.cs`、`modules/abstractions/hands/shell/ChatCommandArgAttribute.cs:8-53`、`modules/tools/cli_nodes/groups/file/private/FileCommands.cs:36-175` |
| 现有生成器边界 | `CliOptionGenerator` / `ParamMetadataGenerator` 是增量生成器，不是 DiagnosticAnalyzer；不能直接承担编译错误 | `modules/generators/cli_option_generator/CliOptionGenerator.cs`、`modules/generators/param_metadata_generator/ParamMetadataGenerator.cs` |
| 工具 exe 分析器接入 | 当前显式关闭全仓分析器，新分析器必须单独接入 | `modules/tools/cli_nodes/cli_node.props:21-29` |
| Bypass 语义 | 当前 agent restriction 与 tool filter 都直接放行 Bypass；新硬权限不能照搬该短路 | `modules/guard/guard_permission/tool_handlers/policy/AgentRestrictionMiddleware.cs:24-49`、`modules/guard/guard/permission/utils/ToolFilterPolicy.cs:11-17` |

## 剩余实施阶段

### 阶段 0：批准 ADR

- [ ] 用户批准集中 broker、direct exe 需执行授权、硬权限不被 Bypass 推翻、参数分析器覆盖边界。
- [ ] ADR 状态从 proposed 转为 accepted。
- [ ] 本任务状态从“等待批准”转为“实施中”。

### 阶段 1：file 组垂直切片

- [x] 定义权限 YAML schema、AOT 解析器和策略 DTO。
  - `CliToolPolicy.cs` / `CliToolPolicyLoader.cs`；契约测试 12/12。
- [x] file 组内嵌 `policy.yaml`。
  - `resources/tools/policies/file.yaml` + `jcc_tool_file.csproj` 的 `EmbeddedResource`/`AdditionalFiles`。
  - 可信构建目录（policy 哈希目录）未实现，见阶段 5。
- [x] 实现 jcc 授权 broker 与跨进程授权上下文。
  - `CliAuthorizationBroker.cs`：本机命名管道 + CurrentUserOnly + 随机 token。
  - **偏离 ADR 原文**：当前签发的是「作用域会话 token」，不是一次性 grant；broker 每次调用都用实际参数重新判定。
    重放等同于同一 agent 在自己工具域内再次调用，不越权；一次性 grant（绑定参数摘要/nonce/策略哈希）留作阶段 2 加固。
- [x] `CliToolCommand` 在 local/delegate 分叉前接入 broker。
  - `LaunchAsync` 注入 `JCC_CLI_AUTH_PIPE/TOKEN`；无继承会话时启动进程内全量 broker（手工 `jcc tool call` 路径）。
- [x] `CliNodeRunner` 在 local/delegate 执行前校验授权；`-h`/`--list-local` 免授权。
  - `ExitAuthorizationRequired = 126`；`Auth.Missing`/`Policy.*` 均带正确写法诱导。
  - 迁移门禁：exe 未嵌入策略时按 `Policy.NotEnforced` 保持旧行为，避免其余 12 组被误伤。
- [x] 接入 `CliPermissionAnalyzer`，覆盖 file local handler 的参数读取。
  - `JCC13001/13002/13004/13008`；帮助里有、handler 未读的参数用 `deny` 墓碑登记（`read.pages`）。
- [x] 红测试：漏登记、多登记、别名、动态参数键、直接 exe、伪造 token。
  - 分析器 13/13；CLI 契约层 119/119（含 13 份策略全量解析）；shell 环境注入 3/3；Hands.Shell 全量 563/563。
- [x] 真机：主代理路径。
  - `jcc tool call file read --file_path agents.md` → 0。
  - `--pages 1` → 126 `Policy.Deny`；`--bogus x` → 126 `Policy.Parameter`。
  - 直接 `jcc-tool-file.exe read` → 126 `Auth.Missing`；无策略组 `jcc-tool-search.exe glob` → 0（旧行为保持）。
- [ ] 真机：Explore / Plan / Bypass / host ask 四条路径（需要真实子代理会话，留待阶段 4 联调）。

### 阶段 2：全量 local / delegate

- [x] 每组一份规范策略源：`resources/tools/policies/<group>.yaml` 共 13 份，由
  `Generate-CliToolPolicies.py` 从 help.txt 生成骨架，人工收紧 deny/能力。
- [x] 分析器接入所有 13 个组 exe（`AdditionalFiles` + Analyzer ProjectReference）。
- [x] 完成 local handler 参数闭包分析：helper 链多层转发、嵌套调用、`GetValueOrDefault`、
  slash 组带引号工具 ID、行尾注释、拒绝墓碑均已覆盖（分析器 13/13）。
- [x] 未知参数在授权前拒绝：broker `Policy.Parameter`（真机 `--bogus` → 126）。
- [x] delegate exe（jcc-gh）自身内嵌策略与 GitHub → jcc-gh 委托链的授权/审计闭环。
  - `jcc-gh` 嵌入 `resources/tools/policies/github.yaml`（executables 增加 `jcc-gh.exe`）；`GhSubCommand.InvokeAndOutputAsync` 调 `CliGroupToolAuthorization` 自证，直跑无会话 126 `Auth.Missing`。
  - `jcc gh` 包装器在无继承会话时由 jcc 进程自建全量会话注入，人工路径不回归。
  - 真机：`jcc-gh auth status` 直跑 126；受限 scope 126 `Agent.AllowedTools`；放行 scope 0；`jcc gh` / `jcc tool call github` 人工 0、受限 126。
- [ ] 一次性 grant（参数摘要/nonce/策略哈希）加固。
- 实施中发现并修复的契约漂移：media/system 的 camelCase help 参数被 handler 按 snake_case 读取、
  多个"帮助里有、handler 不读"的参数、`task_update`/`todo_update` 的 foreach 动态键、
  `remote_trigger` 本地实现与声明契约不符。

### 阶段 3：host / slash / base / launcher

- [x] host 工具由父宿主 broker 授权并在原任务上下文执行。
  - `CliHostToolAuthorization`（公共门面）→ `CliToolAuthorizationClient.AuthorizeHostToolAsync` → broker `ScopeOnly` 判定；无注入环境时 `Host.Manual` 放行。
  - 接入点：`McpCliCommand.ExecuteAgainstRegistryAsync` 启动宿主前；`jcc tool call <组> <host工具>` 转发同样经此路径。
  - 真机双端：受限 scope（AllowedTools 不含 `ctx_inspect`）`jcc mcp_call ctx_inspect` → 1 `Agent.AllowedTools`；分组转发同样拒绝；放行 scope 两条路径均 0；伪造 pid/token → `Auth.Transport`。
- [ ] slash 命令全部具备可分析的 `[ChatCommandArg]` 或显式空参数声明。
  - 部分完成：`jcc slash_call` 已加作用域门禁（`CliHostToolAuthorization` + canonical 命令名），封住子代理绕过分组 exe 直接执行斜杠命令；参数级结构化对账仍待办。
- [ ] base 工具与 jcc launcher 参数纳入各自内嵌策略。
- [ ] `[CliOption]` 的长短别名、否定形式、环境变量映射全部覆盖。
- [ ] MCP 方法参数、必填项、默认值、枚举与别名完成对账。

### 阶段 4：subagent 工具域与发现同源

- [x] 修复调用方 AllowedTools 覆盖角色定义的问题，改为交集。
  - `ContextSetupMiddleware.MergeAllowedTools`；6 个定向测试 + 既有 24 个 ContextSetup 测试全绿。
- [x] 明确“输入空集合 = 未指定”与“解析后空集合 = 拒绝全部”的边界。
  - `ToolDomainConstants.NoToolsPermitted` 哨兵：交集为空时写入不可能存在的工具名，避免空列表被过滤层解释为“不限制”。
- [x] 首轮组目录 `PromptToolGroupScope` 携带有效 CLI 工具域。
  - `cli_groups.yaml` 每组新增 `tool_ids`（生成器现算）；`PromptToolGroupScope.Domain` 由 `SystemPromptMiddleware` 从 `SubAgentContext` 注入；`ToolsSection` 只列可见组、组内数字改为可见数。
  - 单测：受限 `read` 只列 file 组、交集为空隐藏全部组、deny-only 保留其余组（Prompts 232/232）。
- [x] `jcc tool tree` / `find` / `help` 与执行门禁消费同一有效域。
  - broker 新增 `ScopeSnapshot` 只读请求；`CliHostToolAuthorization.GetScopeAsync` + `CliToolScopeFilter` 按 AllowedTools/DeniedTools 裁剪节点与工具，空组自动消失。
  - 真机：受限 scope `read/edit` → tree 仅 file 组 2 个工具、git help 拒绝；`gh_auth_status` scope → github 组仅 1 个工具；无会话 → 13 组 635 个工具全量。
- [ ] 禁止参数从 agent 可见帮助中隐藏；ask 参数保留可见性并标注需确认。

### 阶段 5：全仓收敛与验收

- [ ] 仓库门禁验证每个策略工具恰好由一个参数声明所有者分析器覆盖。
- [x] 审计包含 agent/session、exe、tool、规范化参数摘要、策略版本/哈希、拒绝层。
  - broker `WriteAudit`：`JCC_CLI_AUDIT_LOG` 指定路径时逐判定追加 JSONL（旁路失败不改判定）。
  - 字段：timestamp、agent/session、permissionMode、group、tool、executable、policy id/version、参数名、参数 SHA-256 摘要、allowed、layer、reason；策略哈希待信任目录，grant redeem 在会话 token 模型下不适用。
  - 单测断言放行/拒绝两条记录与摘要差异；真机 probe 落 12 条（host 路径 PolicyId 为空、github 路径 `cli.github`）。
- [ ] 跨域契约变更后运行 `Build-AllDomains.ps1` 全仓编译。
- [ ] main agent 运行受影响单元测试、分析器 fixture、真实双端进程测试。
- [ ] ADR 补实现证据与手动 exe 验收记录。

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 权限 YAML schema + AOT 解析器 | 13 个组 exe | ✅ `CliToolPolicyLoader`；13 份策略 | ✅ 全部组 exe 编译并通过 `Verify-ToolNodes.ps1 -Sample` |
| 可信 exe / policy 哈希目录 | jcc 授权 broker | ❌ | ❌ 未开始 |
| 跨进程授权上下文 | Bash / PowerShell 子进程环境 | ✅ `SystemActuatorBase.AppendCliAuthorizationEnvironment`；3/3 测试 | ❌ 真机子代理 shell 待阶段 4 |
| 集中授权 broker | `CliToolCommand` local/delegate 路径、`McpCliCommand`/`SlashCallExecutor` host 路径、`CliHostToolAuthorization` | ✅ 命名管道 + 作用域 token + `ScopeOnly` 判定；124/124 测试 | ✅ `jcc tool call file read/write` 真机放行与拒绝；host/slash 受限与放行真机双向验证 |
| delegate 自证 | `jcc-gh` 内嵌 github 策略 + `CliGroupToolAuthorization` | ✅ `GhSubCommand` 执行前判定；jcc gh 人工会话注入；Gh 800/800 测试 | ✅ 直跑 126 `Auth.Missing`；受限 126 `Agent.AllowedTools`；`jcc gh`/`jcc tool call github` 人工 0、受限 126 |
| slash 直调门禁 | `SlashCallExecutor` | ✅ canonical 命令名 + `ScopeOnly` | ✅ 受限 scope `/compact` 拒绝，放行 scope `/version` 0，人工直调 0 |
| 一次性执行 grant + redeem | `CliNodeRunner` local / delegate | ❌ 当前为作用域会话 token（ADR 偏离，待阶段 2 加固） | ❌ |
| 参数权限硬规则 | broker `CliToolPolicyEvaluator` | ✅ 未知参数/deny 参数/工具域/拒绝集 | ✅ `--pages`→126 `Policy.Deny`；`--bogus`→126 `Policy.Parameter` |
| 迁移门禁（无策略 exe 旧行为） | `CliToolAuthorizationClient.AuthorizeCoreAsync` | ✅ `Policy.NotEnforced`；3/3 门禁测试 | ✅ 各组直跑统一 126；delegate/host 无策略 exe 仍按旧行为 |
| Roslyn 参数对账分析器 | 13 个组 csproj | ✅ JCC13001/13002/13004/13008；13/13 测试 | ✅ 13 组编译零诊断；119/119 契约测试 |
| subagent 有效工具域 | `ContextSetupMiddleware`、`SubAgentContext` | ✅ 交集 + 空交集哨兵；6/6 测试 | ❌ 真实子代理会话待阶段 4 |
| 发现与执行同源 | `ToolsSection`、`CliToolCommand` tree/find/help、`CliToolScopeFilter` | ✅ `ScopeSnapshot` + 首轮组目录裁剪（`tool_ids`）；8 个过滤测试 + 3 个提示词测试 + broker 快照测试 | ✅ 受限 tree/help/find 真机裁剪，无会话全量；首轮提示词单测覆盖，真实子代理会话待阶段 4 联调 |
| 权限审计与诱导错误 | broker `WriteAudit`（`JCC_CLI_AUDIT_LOG`）+ CLI stderr | ✅ JSONL 字段含 agent/session/参数名/摘要/策略版本/层；策略哈希归入信任目录 | ✅ 真机 12 条审计（host 与 github 路径）；单测断言放行/拒绝记录 |

## 非目标

- 不把 CLI 授权网关伪装成操作系统沙箱；任意 shell 能力仍受现有 Bash 权限管线约束。
- 不新增第二套权限判定引擎；AI Judge、ask 队列、危险命令检测继续复用现有实现。
- 不把完整工具 schema 或 `-h` 文本重新注入首轮提示词。
- 不在 ADR 批准前修改生产代码、分析器或 YAML。
