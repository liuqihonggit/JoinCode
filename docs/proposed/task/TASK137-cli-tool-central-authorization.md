# TASK137 — CLI 工具集中鉴权与参数权限编译期门禁

- 状态：**ADR 起草完成，等待用户批准；未开始实现**
- 日期：2026-11-25
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

- [ ] 定义权限 YAML schema、AOT 解析器和策略 DTO。
- [ ] file 组内嵌 `policy.yaml`，jcc 内嵌可信构建目录。
- [ ] 实现 jcc 授权 broker、跨进程授权上下文与一次性执行 grant。
- [ ] `CliToolCommand` 在 local/delegate/host 分叉前接入 broker。
- [ ] `CliNodeRunner` 在 local/delegate 执行前 redeem grant；`-h` 保持免授权。
- [ ] 接入 `CliPermissionAnalyzer`，覆盖 file local handler 的参数读取。
- [ ] 红测试：漏登记、多登记、别名、动态参数键、直接 exe、重放 grant、篡改策略。
- [ ] 真机：主代理、Explore、Plan、Bypass、host ask 五条路径。

### 阶段 2：全量 local / delegate

- [ ] 每组一份规范策略源；同一策略可嵌入组 exe 与 delegate exe。
- [ ] 分析器接入所有组 exe 与 delegate exe。
- [ ] 完成 local handler 注册表到方法体的参数闭包分析。
- [ ] 未知参数在授权前拒绝，不能进入 handler 才报错。
- [ ] GitHub → jcc-gh 委托链的授权、兑现与审计闭环。

### 阶段 3：host / slash / base / launcher

- [ ] host 工具由父宿主 broker 授权并在原任务上下文执行。
- [ ] slash 命令全部具备可分析的 `[ChatCommandArg]` 或显式空参数声明。
- [ ] base 工具与 jcc launcher 参数纳入各自内嵌策略。
- [ ] `[CliOption]` 的长短别名、否定形式、环境变量映射全部覆盖。
- [ ] MCP 方法参数、必填项、默认值、枚举与别名完成对账。

### 阶段 4：subagent 工具域与发现同源

- [ ] 修复调用方 AllowedTools 覆盖角色定义的问题，改为交集。
- [ ] 明确“输入空集合 = 未指定”与“解析后空集合 = 拒绝全部”的边界。
- [ ] `PromptToolGroupScope` 携带有效 CLI 工具域。
- [ ] 首轮组目录、`tree` / `find` / `help`、执行门禁消费同一有效域。
- [ ] 禁止参数从 agent 可见帮助中隐藏；ask 参数保留可见性并标注需确认。

### 阶段 5：全仓收敛与验收

- [ ] 仓库门禁验证每个策略工具恰好由一个参数声明所有者分析器覆盖。
- [ ] 审计包含 agent/session、exe、tool、规范化参数摘要、策略版本/哈希、拒绝层。
- [ ] 跨域契约变更后运行 `Build-AllDomains.ps1` 全仓编译。
- [ ] main agent 运行受影响单元测试、分析器 fixture、真实双端进程测试。
- [ ] ADR 补实现证据与手动 exe 验收记录。

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 权限 YAML schema + AOT 解析器 | 各工具组 exe、delegate exe、jcc base/launcher | ❌ | ❌ 未开始 |
| 可信 exe / policy 哈希目录 | jcc 授权 broker | ❌ | ❌ 未开始 |
| 跨进程授权上下文 | Bash / PowerShell 子进程环境 | ❌ | ❌ 未开始 |
| 集中授权 broker | `CliToolCommand`、host 工具执行 | ❌ | ❌ 未开始 |
| 一次性执行 grant + redeem | `CliNodeRunner` local / delegate | ❌ | ❌ 未开始 |
| 参数权限硬规则 | jcc `CliToolAuthorizer` | ❌ | ❌ 未开始 |
| Roslyn 参数对账分析器 | jcc、13 个组 exe、delegate exe | ❌ | ❌ 未开始 |
| subagent 有效工具域 | `ContextSetupMiddleware`、`SubAgentContext` | ❌ | ❌ 未开始 |
| 发现与执行同源 | `ToolsSection`、`CliToolSearch`、`CliToolHelp` | ❌ | ❌ 未开始 |
| 权限审计与诱导错误 | broker、CLI stderr、JSONL 审计 | ❌ | ❌ 未开始 |

## 非目标

- 不把 CLI 授权网关伪装成操作系统沙箱；任意 shell 能力仍受现有 Bash 权限管线约束。
- 不新增第二套权限判定引擎；AI Judge、ask 队列、危险命令检测继续复用现有实现。
- 不把完整工具 schema 或 `-h` 文本重新注入首轮提示词。
- 不在 ADR 批准前修改生产代码、分析器或 YAML。
