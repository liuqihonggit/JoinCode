# 0168 — CLI 工具集中鉴权、嵌入参数策略与编译期对账

- 状态：**accepted（2026-11-26 验收通过；无遗留加固项）** — 13 组策略已内嵌并受信任目录哈希锚定；local/delegate/host/slash_call 四入口受 broker 约束，slash 直调含参数白名单与结构化 schema 校验（required/enum/type）；首轮组目录与 tree/find/help 同源；真实子代理 explore/plan 在 bypass 模式下仍被 CLI 域拒绝，code 变体获得更宽工具树（7 vs 5）；放行判定签发一次性 grant（绑定参数/ exe/策略哈希/令牌，重放 fail-closed）；agent 会话内启动器 `--exe/--dir/--resume` 由内嵌 base 策略拒绝（126 `Base.AgentForbidden`）；agent shell 嵌套启动 jcc 被 launcher 守卫拦截（126 `Host.Launcher`）；JSONL 审计可选开启
- 日期：2026-11-25（2026-11-26 更新实施证据）
- 决策者：用户（2026-11-25 要求先出 ADR；2026-11-26 授权自主实施）
- 上游：[ADR 0157](../../implemented/adr/cli_jcc/0157-tool-exposure-data-driven-node-cli.md)
- 关联：[ADR 0162](0162-task-scoped-permission-overlay.md)、[ADR 0163](0163-ai-permission-judge-and-autonomous-mode.md)、[TASK137](../task/TASK137-cli-tool-central-authorization.md)
- 影响域：`apps.cli`、`tools.cli_nodes`、`guard.permission`、`mcp_tool_dispatch`、`llm.agents.*`、`cognition.prompts`、`cognition.hands_shell`、`generators`

## 背景

当前多 CLI.exe 架构已经满足“首次提示词薄、详情按需读取”：

- `resources/tools/base.yaml:22-191` 是首轮协议原生 tools 的唯一来源，当前包含 read/write/edit/bash 与 agent 内部基础工具，不再是固定 4 个工具。
- `resources/tools/cli_groups.yaml:7-85` 只向首轮提示词提供 13 个组的 ID、exe、数量与摘要。
- `ToolsSection.cs:29-107` 保留 `<exe> -h`、`<exe> -h <关键词>`、`<exe> <工具> -h` 的下钻方式，不把完整 schema 注入首轮。
- `CliNodeRunner.cs:114-161` 统一处理组 exe 的帮助、local、delegate 与 host 分叉。

但权限边界没有跟上工具拆分：

| 现状 | 代码证据 | 后果 |
|---|---|---|
| `jcc tool call` 只检查 profile 组门禁，然后启动 exe | `CliToolCommand.cs:140-168` | 不判断当前 subagent、工具参数或权限模式 |
| local handler 在组 exe 内直接执行 | `CliNodeRunner.cs:210-225` | 完全绕过 jcc 与宿主权限管线 |
| delegate 直接启动另一 exe | `CliNodeRunner.cs:236-315` | 委托链没有授权上下文 |
| host 路径复用 `mcp_call` / `slash_call` | `CliToolCommand.cs:170-190`、`McpCallCommand.cs:16-45` | 有宿主权限检查，但与 local/delegate 不是同一入口 |
| 组 `node.yaml` 只有 id/mode/aliases | `modules/tools/cli_nodes/groups/file/node.yaml` | 没有参数权限数据 |
| 工具组 exe 关闭全仓分析器 | `modules/tools/cli_nodes/cli_node.props:21-29` | 新分析器不会自动覆盖这些项目 |
| 子代理权限只在 AsyncLocal 中 | `SubAgentContext.cs:41-57` | Bash 启动 `jcc tool call` 子进程后上下文丢失 |
| 调用方 AllowedTools 非空时覆盖角色定义 | `ContextSetupMiddleware.cs:213-226` | 调用方请求可能放大角色权限 |
| Bypass 直接跳过 agent restriction | `AgentRestrictionMiddleware.cs:24-49`、`ToolFilterPolicy.cs:11-17` | 新硬权限不能复用这一短路语义 |

ADR 0157 已明确记录 local bash/powershell 绕过宿主 Shell 中间件栈的风险（`0157-tool-exposure-data-driven-node-cli.md:265`）。本 ADR 不掩盖该边界，而是为 CLI 工具调用增加独立于工具实现进程的集中授权面。

**launcher 守卫的已知边界**：守卫按 `JCC_CLI_AUTH_PIPE` 判定「当前位于 agent shell」。拥有任意 shell 的代理可以显式清除该环境变量后再启动 jcc，从而拿到一个新的手工会话；这属于「agent 可执行任意命令」的固有权能，无法在 jcc 进程内彻底关闭。守卫拦截的是正常继承（子进程自动携带凭据）与误用，不是对抗已具备任意命令执行且有意规避的代理；真正的强制隔离需要 OS 级沙箱/容器，超出本 ADR 范围。

## 目标与非目标

### 目标

1. 每个 CLI 可执行文件内嵌权限 YAML，参数权限显式可审计。
2. `jcc.exe` 是唯一权限决策中心；工具 exe 只兑现授权，不解释角色、profile 或权限模式。
3. subagent 工具域遵循最小权限交集，调用方不能放大父级或角色权限。
4. 编译期同时拒绝“有参无权”和“有权无参”，覆盖别名与间接参数入口。
5. 首轮提示词、发现、帮助和执行使用同一有效工具域。
6. local、delegate、host 三条路径具有一致的授权、审计与错误语义。

### 非目标

- 不把 CLI 网关声明为操作系统沙箱。任意 shell 命令、同用户恶意进程和已获授权的危险命令仍超出网关隔离能力。
- 不新增第二套权限引擎。危险命令、路径边界、ask、AI Judge、临时批准继续复用现有权限管线。
- 不把完整工具 schema 或 `-h` 输出重新注入首轮提示词。
- 不要求为无参数工具伪造参数条目；无参数工具必须显式声明空参数集合。

## 决策

### 1. 在 jcc 宿主内建立唯一授权 broker

新增 `ICliAuthorizationBroker` 及默认实现 `CliAuthorizationBroker`，由 jcc 宿主持有。broker 是唯一负责下列事项的组件：

1. 解析当前 agent/session 的有效工具域。
2. 读取并验证目标 exe 内嵌策略与可信构建目录。
3. 规范化工具别名、参数别名和环境变量来源参数。
4. 执行不可绕过的 CLI 硬策略。
5. 调用现有 `PermissionChecker` 决定剩余 allow/ask/deny。
6. 按当前会话作用域返回授权判定（allow 或带诱导的 deny），或拒绝并给出可诱导错误。
7. 写统一审计记录。

工具组 exe 不读取策略做角色判断，也不实现 allow/ask/deny 聚合。它只携带 opaque 会话 token 向 broker 请求判定；broker 每次都用该 exe 上报的实际工具与规范化参数重新评估，token 本身不含权限真相。

> 实现注记（2026-11-26）：file 切片落地的是“作用域会话 token + 每调用重判”，不是一次性 grant。
> 重放在语义上等价于同一 agent 在自己工具域内再次调用，不越权；参数被替换也只会走完整策略判定。
> 一次性 grant（绑定参数摘要、exe 身份、策略哈希、nonce 与短过期）保留为阶段 2 的加固项，
> 用于把“同一 agent 的任意调用”进一步收紧为“宿主批准过的那次调用”。

#### 为什么必须跨进程传递授权上下文

当前 agent 通过 Bash/PowerShell 执行 `jcc tool call`。`BashSystemActuator.cs:195-199` 只注入 SHELL，`SystemActuatorCommandContext.cs:142-148` 继承并清理进程环境；`SubAgentContext` 的 AsyncLocal 不会随 `Process.Start` 传播。

因此只在 `CliToolCommand` 中读取 `SubAgentContext.Current` 是错误设计：新 jcc 子进程读到的是空上下文，会被误判为普通用户调用。

本决策采用：

- 父宿主为每个 agent 生成随机、短期、不可伪造的 broker 会话凭据，并注入其 Bash/PowerShell 子进程环境。
- 凭据不包含权限真相，只标识 broker 会话；权限域由 broker 从父宿主状态解析。
- broker 通过本机命名管道 / Unix domain socket 提供服务，socket 使用当前用户 OS ACL。
- 子进程 `jcc tool call` 携带会话凭据向父 broker 发起授权请求。
- 父进程退出、凭据过期、broker 不可达时，agent 发起的执行 fail-closed；`-h` 仍可读。
- 直接由用户启动且无 agent 会话凭据的 `jcc tool call`，在 jcc.exe 内建立临时 broker，按当前用户权限配置交互决策。

shell 环境清洗器必须显式保留授权凭据变量，同时禁止把 broker 密钥或完整权限配置注入子进程。

### 2. 执行流程统一为“先授权，后实现”

| 模式 | 授权与执行流程 |
|---|---|
| local | `jcc tool call` → 父 broker 会话凭据注入环境 → 启动组 exe → 组 exe 携带实际工具/参数请求判定 → 调用 local handler |
| delegate | 组 exe 向同一 broker 判定后启动 delegate exe；delegate 侧再按自身内嵌策略自证（`CliGroupToolAuthorization`），子进程继承同一会话凭据，两层都不放行越域调用 |
| host | broker 在父宿主的原任务上下文授权，并通过现有 `PermissionAwareToolExecutor` / slash 执行链执行；`jcc mcp_call`、`jcc tool call <组> <host工具>`、`jcc slash_call` 共用 `ScopeOnly` 判定 |
| help / tree / find | 不请求执行判定；有会话时经 `ScopeSnapshot` 读取工具域并裁剪节点/工具，无会话时返回全量 |

关键约束：

- `CliToolCommand.RunCallAsync` 在 local/delegate/host 分叉前进入 broker，不再只做 profile 组门禁。
- host 工具由父宿主执行，避免子进程丢失 session、agent、ask 队列与 Hub 状态。
- 会话凭据绑定 agent/session 与有效工具域；判定输入是 canonical tool + 规范化参数 + 目标 exe 身份。
- 凭据通过环境传递，不放在命令行参数中；每次执行都重新判定，不复用上一次的允许结果。
- direct 运行组 exe 执行工具时，如果没有有效会话凭据，返回授权要求与 `jcc tool call` 正确用法；`-h` 和 `--list-local` 不需要凭据。
- host 工具没有组内嵌策略，授权走 `CliHostToolAuthorization` → broker `ScopeOnly`（只看 AllowedTools/DeniedTools 与 token 作用域）；接入点在 `McpCliCommand.ExecuteAgainstRegistryAsync`，覆盖 `jcc mcp_call` 与 `jcc tool call <组> <host工具>` 两条入口；未注入凭据（人工直跑）按 `Host.Manual` 放行。
- 因此 ADR 0157 的“独立 exe”仍表示物理进程隔离，但不再表示“可绕过 jcc 直接执行”。这是本 ADR 对 0157 安全边界的明确收窄。

### 3. 权限 YAML 的单一来源与嵌入方式

规范策略源放在 `resources/tools/policies/`，由 MSBuild 嵌入对应 exe：

| 策略 | 规范源 | 嵌入位置 |
|---|---|---|
| 基础工具 | `base.yaml` | jcc.exe |
| launcher 元命令参数 | `base_policy.yaml`（不参与 policy_trust） | jcc.exe |
| 每个工具组 | `<group>.yaml` | `jcc-tool-<group>.exe` |
| delegate 实现 | 与所属组共用同一 `<group>.yaml` | 同时嵌入 `jcc-tool-<group>.exe` 与 delegate exe |

同一策略文件可以被多个 exe 嵌入，但只保留一份规范源，不复制内容。`node.yaml` 继续负责工具身份、mode 与 alias；策略 YAML 只负责权限，不重复维护说明、类型、必填项和默认值。

运行时 jcc 通过 `System.Reflection.Metadata.PEReader` 只读提取目标 exe 的 manifest resource，不 `Assembly.LoadFrom` 目标 exe。提取过程只读取 PE 元数据，不执行目标代码。

嵌入不等于可信。jcc 同时内嵌由构建过程生成的 `CliToolTrustCatalog`，至少记录：

- policy id 与规范策略哈希；
- 允许的 exe 文件名；
- 发布构建中的 exe SHA-256；
- 策略声明的所有者项目与分析器覆盖项目；
- schema version。

agent 调用只允许注册 exe。`--exe`、`JCC_TOOL_EXE_DIR` 或 PATH 解析出的文件必须与目录身份一致；不得用“目标 exe 自报的 policy”作为授权真相。策略或 exe 哈希不匹配时 fail-closed。工具构建必须先于信任目录与 jcc 发布构建，目录生成物是派生数据，不是第二份权限源。

实现进度：`policy_trust.yaml` 由 `Generate-CliToolPolicies.py` 生成（policy id + 策略 SHA-256 + executables），`CliToolPolicyTrustCatalog` 在 jcc 运行时 broker（`enforcePolicyTrust`）校验三项；发布 exe SHA-256、所有者项目/分析器覆盖字段与 schema version 留待发布签名阶段。

### 4. 参数权限模型

每个工具必须显式列出全部 canonical 参数；别名不需要重复条目，但分析器必须证明别名能归一到已登记参数。

示例（结构示意，不代表最终字段名）：

```yaml
schema_version: 1
policy_id: cli.file
policy_version: 1
executables:
  - jcc-tool-file.exe
tools:
  read:
    effect: allow
    parameters:
      file_path:
        effect: allow
        capabilities: [workspace_read]
      offset:
        effect: allow
        capabilities: []
      limit:
        effect: allow
        capabilities: []
  write:
    effect: ask
    parameters:
      file_path:
        effect: allow
        capabilities: [workspace_write]
      content:
        effect: allow
        capabilities: [workspace_write]
  file_delete:
    effect: deny
    parameters:
      file_path:
        effect: allow
        capabilities: [filesystem_delete]
```

规则：

- `effect` 只允许 `allow` / `ask` / `deny`。
- `capabilities` 使用枚举 + `[EnumValue]` 作为唯一字符串真相源，例如 workspace_read、workspace_write、filesystem_delete、network_read、network_write、process_execute、session_mutate、permission_mutate。
- 参数登记至少包含 effect 与 capabilities；禁止依赖未声明的“默认安全”。
- 工具 effect 与本次实际出现参数的 effect 共同聚合：`deny > ask > allow`。
- capabilities 用于复用现有路径、命令、网络与敏感操作检测，不在 YAML 中编写命令正则或路径黑名单。
- 必填参数、默认值、枚举与类型继续来自现有 schema/特性/帮助目录；权限层只消费规范化后的有效参数。
- `--args-file`、`--args-stdin`、JSON、key=value 最终都先转换为 canonical 参数字典，再授权；授权后不得重新解释出未授权参数。
- 环境变量映射参数在决策前纳入有效参数，不能绕过 YAML。
- 未登记参数、未知工具、未知 alias、重复 canonical 名都是错误，不做静默忽略。

### 5. 硬权限与现有权限管线的关系

授权顺序固定为：

1. 验证 broker 会话、exe 身份、策略完整性与版本。
2. 计算 agent 有效工具域，执行工具级硬拒绝。
3. 规范化参数，执行参数级硬拒绝与能力检查。
4. 调用现有 `PermissionChecker`，由现有中间件决定动态风险、ask、AI Judge 与临时批准。
5. 返回 allow 判定或带诱导的拒绝。

不可绕过约束：

- CLI 硬 deny、agent DeniedTools、工具域交集、策略完整性检查发生在 `PermissionMode.Bypass` 之前。
- Bypass 只影响既有权限管线中的交互/动态风险语义，不解除本 ADR 的硬边界。
- AI Judge 不能推翻 CLI 硬 deny。
- allow 参数不能覆盖 agent 工具域拒绝；ask 参数不会绕过工具域拒绝。
- host 路径保留现有执行管线的纵深防御，但不得再次解释 YAML 或产生另一份相互冲突的授权结果。

### 6. subagent 有效工具域只能收窄

在 `ContextSetupMiddleware` 解析角色定义时计算显式有效域：

```text
Effective(agent) =
    Effective(parent)
    ∩ Normalize(caller AllowedTools 或“未指定”)
    ∩ Normalize(role AllowedTools 或“未指定”)
    \ (caller DeniedTools ∪ role DeniedTools)
    ∩ EnabledProfileGroups
    ∩ PolicyAllowedTools
```

约束：

- 调用方 AllowedTools 只能收窄角色定义，不能像当前 `MergeAllowedTools` 一样覆盖并放大角色权限。
- 输入层空集合表示“未指定”；解析后的有效集合为空表示“拒绝全部”。二者必须是不同类型或显式状态，不能继续靠同一个空 List 猜测。
- 角色定义的 null/空语义必须保留到解析边界，不能在 `DefinitionResolutionMiddleware` 投影时丢失。
- 父级已拒绝的工具不能因子代理角色或 YAML allow 恢复。
- 有效域写入 `SubAgentContext` 或 ADR 0162 的目标态 `PermissionScope`，不新建第二套 AsyncLocal 权限上下文。
- 内置角色档案继续是角色意图来源，不再维护一份与 CLI 工具集等价的手写映射表；CLI policy 只做能力约束，不替代角色职责定义。

### 7. 首轮提示词、发现、帮助、执行同源

`PromptToolGroupScope` 从仅携带 profile Enabled 组扩展为携带有效 CLI 工具域。首轮 `ToolsSection` 只在至少一个工具可见时输出该组。

- `tree` / `find`：只返回有效域内工具。
- `jcc tool help` / `<exe> <tool> -h`：有 agent 上下文时只显示可见工具与参数。
- 硬 deny 参数从 agent 帮助中隐藏；ask 参数可见并标注需要确认。
- 直接 `-h` 是元数据读取，不要求执行授权，可用于用户离线排障。
- 执行门禁与帮助渲染必须调用同一个有效域解析服务，不能各自维护裁剪逻辑。

这保持首轮提示词薄，同时避免“模型看得见但永远执行失败”或“帮助里暴露了不该拥有的能力”。

### 8. Roslyn 分析器：源码参数与 YAML 权限双向对账

新增独立 `CliPermissionAnalyzer`（DiagnosticAnalyzer），不复用 `CliOptionGenerator` / `ParamMetadataGenerator` 的生成器身份，也不把权限策略塞进通用 AOT 规则。

分析器在每个参数声明所有者项目中运行，并通过 `AdditionalFiles` 读取该工具所属的规范策略源与 node 映射：

| 声明来源 | 分析位置 | 提取方式 |
|---|---|---|
| `[CliOption]` | jcc launcher 项目 | 枚举字段、长/短别名、否定形式、AcceptsValue、EnvVar |
| `[McpTool]` + `[McpToolParameter]` | 各 MCP handler 所有者项目 | 方法工具名、参数/属性特性、必填项与 alias |
| `[ChatCommand]` + `[ChatCommandArg]` | slash 命令所有者项目 | 命令名、alias、结构化参数声明 |
| local `CliNodeHandler` | 对应组 exe | handler 注册表 + 参数字典字面量读取闭包 |
| delegate 参数 | delegate exe 所有者项目 | 与 local 相同的结构化声明/字面量读取规则 |

组 exe 只分析 local 工具，不要求它看见 host/delegate 所有者项目的源码；host/delegate 在各自所有者项目中分析。仓库门禁验证所有 CLI 映射工具恰好被覆盖，避免“每个项目都绿但合起来漏项”。

local handler 当前通过 `Required(o, "file_path")`、`GetInt(o, "offset")`、`IsTrue(o, "recursive")`、索引器与 `TryGetValue` 读取参数（`FileCommands.cs:36-175`）。分析器必须：

1. 从 handler 注册字典解析工具 ID 到方法符号。
2. 沿辅助方法调用收集字面量参数键，形成保守闭包。
3. 将 `o[key]`、`TryGetValue(key)` 等动态键视为不可证明，直接报错；不得用猜测补齐。
4. 证明无参数工具没有读取任何参数键。
5. 对共享辅助方法采用保守并集，不能因跨工具调用图复杂而漏报。

斜杠命令的 `ArgumentHint` 与原始字符串拆分不能证明参数集合。凡暴露为 CLI 的 slash 命令，必须完整声明 `[ChatCommandArg]` 或显式声明零参数；继续依赖自由文本解析的命令在迁移前编译失败。

建议诊断编号：

| ID | 条件 |
|---|---|
| JCC13001 | CLI 工具源码有参数，YAML 未登记 |
| JCC13002 | YAML 登记了源码不存在的参数 |
| JCC13003 | 参数 alias 未归一到已登记 canonical 参数 |
| JCC13004 | local handler 使用不可证明的动态参数键 |
| JCC13005 | CLI slash 命令缺少完整结构化参数声明 |
| JCC13006 | 策略引用未知工具、未知 capability 或非法 effect |
| JCC13007 | canonical 参数/工具重复或 alias 冲突 |
| JCC13008 | CLI 映射工具没有任何策略所有者覆盖 |
| JCC13009 | 策略未嵌入对应 exe 或信任目录缺失 |
| JCC13010 | 参数 schema 与权限条目的必填/类型约束矛盾 |

分析器只使用语法树、语义符号和 `AdditionalFiles`，不执行目标程序、不联网、不依赖反射。YAML 解析沿用 `YamlStream` 手工解析风格，保持 NativeAOT 兼容。

### 9. 审计与错误提示

每次授权记录统一 DTO，经 JsonContext 序列化，至少包含：

- timestamp、sessionId、agentId、invocationId；
- group、canonical tool、exe 身份；
- 参数名与脱敏摘要，不记录 content、token、完整凭据；
- policy id/version/hash；
- 有效权限模式；
- allow/ask/deny、触发层、规则 ID、grant 是否 redeem。

实现说明：broker 在 `JCC_CLI_AUDIT_LOG` 指定路径逐判定追加 JSONL（旁路写失败不改变判定）；
参数只记录名称与 SHA-256 摘要；策略哈希归入信任目录阶段，grant redeem 在会话 token 模型下不适用。

拒绝错误必须满足：

1. 为什么拒绝；
2. 哪个工具/参数触发；
3. 哪个授权层拦截；
4. 正确写法或可用替代工具。

示例：

```text
[CLI-AUTH] denied
tool: file/file_delete
parameter: file_path
layer: HardPolicy.CliParameter
reason: filesystem_delete 对当前 agent 禁用
policy: cli.file@1
next: 使用 jcc tool call file directory_list --directory_path <目录> 先确认内容；写入需求请调用具备 workspace_write 的代理。
```

### 10. 依赖与 AOT 边界

- broker 位于 jcc 宿主，可使用现有 DI、权限管线与审计设施。
- 工具组 exe 仍只引用 `Abstractions.Primitives.Tools` 与必要 BCL；不得引用 guard、引擎或 DI 容器。
- grant redeem 客户端只依赖窄契约与传输实现，不依赖策略解析器。
- 策略读取使用 `YamlStream` 手工解析；审计与 broker RPC 使用 DTO + JsonContext。
- `cli_node.props` 当前关闭全仓分析器，但必须显式添加 `CliPermissionAnalyzer` 作为 Analyzer；不得重新打开整套重型分析器来“顺带覆盖”。

## 替代方案

### A. 每个 CLI exe 自己读取 YAML 并判断权限

放弃原因：直接违背“jcc 是鉴权中心”；每个 exe 都要复制角色、profile、ask 与权限模式语义，且 local/delegate 链会产生多份决定。

### B. 只在 `jcc tool call` 启动前检查

放弃原因：无法覆盖 direct exe；也无法解决 Bash 子进程丢失 AsyncLocal。检查位置正确但边界不完整。

### C. 把完整参数 schema 重新加入首轮提示词

放弃原因：破坏已经验证的薄提示词设计；参数详情已经能通过 `-h` 获取，权限不应以重复 schema 的形式注入。

### D. 只扩展现有 ParamMetadataGenerator

放弃原因：它是增量生成器，不是 DiagnosticAnalyzer；只覆盖 WellKnownParam，覆盖不了 `[CliOption]`、MCP 方法参数、ChatCommand 与 local handler。

### E. 让组 exe 分析器读取所有其他项目源码

放弃原因：破坏工具 exe 依赖墙，且标准 Roslyn compilation 看不到 ProjectReference 源码。采用“声明所有者分析 + 仓库覆盖门禁”更符合项目边界。

### F. 继续允许无会话凭据直接运行 local/delegate exe

放弃原因：agent 可绕过 `jcc tool call`，集中授权形同虚设。独立 exe 的价值是进程隔离，不是无授权执行。

## 分阶段实施

1. **file 垂直切片**：策略 schema、嵌入、broker、作用域会话凭据、local 执行、分析器、真实子代理拒绝/放行。
2. **local/delegate 全量**：13 组策略与 delegate 链，参数闭包分析、未知参数拒绝。
   - delegate 段已完成：`github.yaml` 同时内嵌 jcc-gh，delegate 侧自证 + 直跑 fail-closed；人工 `jcc gh` 由 jcc 进程签发全量会话。
3. **host/slash/base/launcher**：父宿主执行、结构化 slash 参数、全局与元命令参数。
   - host 段已完成；slash 段已完成直调参数白名单（jcc 内嵌 slash.yaml，扁平参数走组策略），`[ChatCommandArg]` 声明 ↔ 生成物 ↔ 策略双向对账已接入 CI（`Verify-SlashCommandPolicies.py`）。
   - launcher 绕过已封：agent shell 中存在 `JCC_CLI_AUTH_PIPE` 时，TUI / `-p` 启动返回 126 `Host.Launcher`，`JCC_ALLOW_NESTED_LAUNCH=1` 显式放开。
   - base/launcher 参数策略已落地：`resources/tools/base_policy.yaml`（内嵌 jcc）约束 agent 会话内的启动器选项 — `tool call` 的 `exe/dir`、`tool tree/find/help` 的 `dir`、`slash_call` 的 `resume` 一律 deny（`Base.AgentForbidden`/126），手工终端无会话时 `Base.Manual` 放行；`CliLauncherPolicyCatalog` 为策略与消费代码唯一登记表，并有双向对账测试（清单↔YAML）。
   - 真机验收：假会话 + `JCC_ALLOW_NESTED_LAUNCH=1` 直跑 `jcc tool call file read --exe` → 126 `Base.AgentForbidden`；`jcc tool tree --dir` → 126；`jcc slash_call --resume` → 拒绝；手动终端同命令不被拦。E2E `NestedLauncher_ExeOverride_DeniedByBasePolicy` 用真实子代理 + broker 验证（工具树仍可用，`--exe` 被拒）。
   - slash 结构化 schema 已落地：真相仍是 `[ChatCommandArg]` → `GeneratedSlashCommandSchemaCatalog`（help.txt 新鲜度对账已覆盖类型/必填/枚举列）；`slash_call` 在执行前按 schema 校验 required/enum/type（`SlashCommandArgValidator`，`Slash.Schema` 层），非法参数返回带正确写法的单行错误。策略 YAML 继续只管参数名白名单（两套真相不重复登记）。
   - 真机验收：`jcc slash_call generate '{}'` → 缺必填 `--description`；`agents {"action":"delete"}` → 枚举拒绝；`ultraplan {"steps":"many"}` → 类型拒绝；`agents action=list` → 正常执行（exit 0）。
4. **有效工具域与发现同源**：修复 AllowedTools 覆盖问题，统一 prompt/tree/find/help/execution。
   - 已完成：tree/find/help 用 `ScopeSnapshot` + `CliToolScopeFilter`；首轮组目录经 `cli_groups.yaml.tool_ids` + `PromptToolGroupScope.Domain` 按 agent 域裁剪。
5. **全仓收敛**：覆盖门禁、审计、跨域全仓编译、真实双端进程验收。
   - 审计段已完成：`JCC_CLI_AUDIT_LOG` JSONL + 真机 12 条；覆盖门禁已并入 `Verify-ToolNodes.ps1 -DeclarationsOnly`（策略/节点/分析器接线/委托内嵌/哈希新鲜度）；信任目录已落地（id+策略哈希+exe 名）。
   - 真实子代理段已落地：E2E 覆盖 explore/plan 只读域 + bypass 模式，并修复 `subagent_type` 未解析为 Role/Variant 导致子代理空工具域（不限制）的缺陷。

每阶段独立编译、测试、提交；跨域契约变更执行 `Build-AllDomains.ps1`。

## 影响

| 模块 | 预期变更 |
|---|---|
| `apps.cli` | `CliToolCommand` 接 broker；信任目录；launcher 策略；manual broker |
| `tools.cli_nodes` | 内嵌 policy；执行前 redeem；direct exe fail-closed；分析器接入 |
| `guard.permission` | 提供硬策略前置边界，复用 PermissionChecker，不新增结果语义 |
| `mcp_tool_dispatch` | host 工具在父任务上下文授权/执行，避免子进程上下文丢失 |
| `llm.agents.*` | AllowedTools 改为交集；保留 null/空语义；有效域进入任务 scope |
| `cognition.prompts` | `PromptToolGroupScope` 携带有效工具域；组目录按工具裁剪 |
| `cognition.hands_shell` | 注入 broker 会话凭据；环境清洗显式保留 |
| `generators` | 新增 `CliPermissionAnalyzer` 与 fixture 测试 |
| `resources/tools` | 新增 policies 与构建信任目录输入 |

## 验收

- [ ] 每个 CLI 工具与参数均能在嵌入 YAML 中定位；无参数工具显式为空。
- [ ] 删除任一参数权限条目，所有者项目编译红灯；补回后绿灯。
- [ ] 增加未知参数/工具/能力，分析器编译红灯。
- [x] 短别名、长别名、否定开关、环境变量参数均通过同一 canonical 权限。
  - 工具组经分析器归一化覆盖别名；jcc 启动器侧改用 `resources/tools/base_policy.yaml`（`CliLauncherPolicyCatalog` 唯一登记表 + 双向对账测试），agent 会话内 `exe/dir/resume` 硬拒绝，E2E 与真机 probe 覆盖。
  - `[CliOption]` 的别名/环境变量逐项覆盖仍暂缓：启动器选项集已由 base 策略登记表约束，别名级对账留待 slash 结构化 schema 一并处理。
- [ ] local 动态参数键与未结构化 slash 命令被编译拒绝。
  - 运行时已拒绝未登记 slash 参数（`Policy.Parameter`）且参数必须是 JSON 对象/key=value；`Verify-SlashCommandPolicies.py` 已做源码声明 ↔ help/node ↔ slash.yaml 双向对账（99 命令 / 101 参数，CI 接入），无参命令显式 `parameters: []`。
  - 结构化校验已接入：`slash_call` 执行前按 `[ChatCommandArg]` 生成的 schema 拒绝缺必填/枚举越界/类型不符；17 条 Host.Tests 覆盖（含真实生成目录的 generate/ultraplan 声明抽查），真机 probe 四项通过。
- [x] Explore/Plan 不能执行 write/edit；调用方尝试扩大角色工具集仍被拒绝。
  - `ContextSetupMiddleware.MergeAllowedTools` 交集测试（6 个定向测试）；broker 侧 `Broker_ReadOnlyScope_DeniesWriteOverPipe` 与 `Gate_SubAgentScopeEnvironment_IntersectsToolDomain` 证明只读 scope 对 write 返回 `Agent.AllowedTools`。
  - 真实会话：E2E `ReadOnlySubAgent_CliHostToolOutsideScope_IsDeniedByBroker`（explore/plan）中，子代理 Bash 启动 jcc，`tool tree` 仅 3 组/5 个只读工具，域外 host 工具被 `Agent.AllowedTools` 拒绝。
  - 实施中发现并修复：Agent 工具此前未把 `subagent_type` 解析为 Role/Variant，子代理以空工具域（不限制）运行；新增 `AgentSubagentTypeResolver` 统一解析并用于 `SubAgentContext` 构造。
- [x] Bypass 不能越过 CLI hard deny、agent DeniedTools 或策略完整性检查。
  - CLI 判定链不读取 `PermissionMode`（见 `CliAuthorizationScope.Evaluate`），Bypass 无法改变结果；`DeniedTools` 命中返回 `Agent.DeniedTools`（`CliToolAuthorizationTests`）；策略完整性由信任目录覆盖。
  - 真实会话：上述 E2E 以 `JCC_PERMISSION_MODE=bypass` 运行，域外工具仍被 `Agent.AllowedTools` 拒绝。
- [x] direct 组 exe 无会话凭据返回授权要求；`-h` 正常。
  - delegate exe 已覆盖：`jcc-gh` 直跑 126 `Auth.Missing`，`-h`/`--version` 不受限；组 exe 直跑行为不变。
- [x] 无效/伪造凭据、越域工具、未登记参数、deny 参数、损坏策略均 fail-closed；参数篡改经完整策略判定而非放行。
  - 测试覆盖：`Broker_RejectsForgedToken`、`UnknownTool_IsDenied`、`UnknownParameter_IsDeniedBeforeHandlerRuns`、`Agent.DeniedTools` 用例、`Parse_UnknownEffect_RejectsInsteadOfDefaultingToAllow`、`Parse_UnknownCapability_RejectsWithToolParameterAndSource`、信任目录哈希不符/未知策略拒绝；真机 `--bogus` → 126 `Policy.Parameter`。
- [x] （阶段 2 加固）一次性 grant：参数摘要、exe 身份、策略哈希、nonce 与过期绑定，重放 fail-closed。
  - broker 在放行时签发 30 秒一次性 grant，绑定令牌摘要、group/tool、exe、参数摘要与策略哈希；执行前由客户端兑换（默认自动兑换，`redeemGrant:false` 仅供测试）；`TryRemove` 先摘除后校验，重放/跨会话/改参/换 exe/策略漂移全部拒绝并记审计。
  - 5 条新测试覆盖：单次兑换与重放、改参消耗、跨会话、过期（可拨动时钟）、scope-only 宿主路径；自动兑换失败即按拒绝返回。
- [ ] host ask 由父宿主 UI/队列处理，不因子进程丢失上下文而自动放行。
  - 当前无确认 UI 的 CLI 入口遇到 `effect: ask` 统一返回 `Policy.Ask` 拒绝并提示回宿主交互会话，不会静默放行（见 `CliAuthorizationScope.EvaluateEffect`）。
- [ ] 首轮组目录、tree/find/help 与执行允许集合一致。
  - tree/find/help 已真机一致；首轮组目录已按 `tool_ids` 裁剪并有单测（Prompts 232/232），E2E 真实子代理首轮按 3 组裁剪，与 tree 输出一致。
- [ ] 真实双端进程测试覆盖主代理、只读子代理、写子代理、delegate、host。
  - host 段已覆盖：受限 scope 下 `jcc mcp_call` 与 `jcc tool call <组> <host工具>` 均 `Agent.AllowedTools` 拒绝；放行 scope 两条路径正常执行；伪造凭据 `Auth.Transport` 拒绝。
  - 只读子代理已覆盖：E2E `ReadOnlySubAgent_CliHostToolOutsideScope_IsDeniedByBroker` 用真实 MockServer + 真实子代理 + Bash 启动 jcc，验证域外 host 工具 `Agent.AllowedTools` 拒绝与只读工具树（3 组/5 工具）。
  - 链路级：`ShellCliAuthorizationEnvironmentTests.GetEnvironmentOverrides_InjectedScope_RestrictsBrokerToolDomain` 用真实 actuator 注入环境再经真实 broker 判定（放行/越域各一条）。
  - 写子代理已覆盖发现侧：E2E `WriteSubAgent_ToolTree_ShowsWiderScopeThanReadOnly`（executor:code）工具树 3 组/7 工具（file 组 3 个写能力工具），只读代理为 3 组/5 工具（file 组 1 个）——授权域按角色/变体裁剪。
  - 写工具实际执行放行由 broker 单测与 host/slash 真机 probe 覆盖（`TodoWrite` 放行、`file write` 策略 allow）。
- [x] 跨域契约变更后 `Build-AllDomains.ps1` 全仓编译通过。
  - launcher 守卫（Tools 常量 + apps.cli 消费）改动后全仓 16 域 + native_tui/native_gui 一次通过。
  - 子代理类型解析修复（模型域新增文件 + Agent 工具消费）后再次全仓编译一次通过。

## 后果

### 正面

- 保持薄首轮提示词与独立进程隔离，同时把权限决策收回 jcc。
- 参数权限从“实现约定”变成编译错误，避免新增参数时静默无授权。
- subagent 职责边界可审计、可测试，不再依赖调用方自觉。
- local/delegate/host 的授权语义统一，审计可以重建完整调用链。

### 负面与风险

- direct exe 不再能离线任意执行工具，用户需经 `jcc tool call`；这是有意的安全权衡。
- broker 增加本机 IPC 与会话凭据生命周期管理，需要处理父进程退出、崩溃与重试；一次性 grant 加固会进一步增加状态。
- 存量 slash 自由文本参数与 local 动态键需要迁移，否则分析器会红灯。
- 工具构建、信任目录与 jcc 构建必须保持顺序；增量开发需明确失效策略。
- 授权网关不是 OS 沙箱，不能宣称阻止已获授权 shell 的所有副作用。

## 与现有 ADR 的关系

- **ADR 0157**：保留多 exe、薄提示词与 `-h` 下钻；收窄 direct exe 无授权执行的安全边界。
- **ADR 0162**：复用任务作用域权限载体，不另建 AsyncLocal 权限上下文；补齐其跨进程传播缺口。
- **ADR 0163**：CLI 硬策略位于 AI Judge 之前，AI 不可推翻；动态灰区判定仍复用现有管线。
