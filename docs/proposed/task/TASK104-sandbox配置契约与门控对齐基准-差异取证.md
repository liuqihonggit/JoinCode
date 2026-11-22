# TASK104 — sandbox 配置契约与门控对齐基准（TASK103 §2.3 #35 取证）

> 状态：**实施中 — Phase 1 最小安全闭环（方案 B 范围）** · 日期 2026-11-21 · 总账编号：TASK103 §2.3 **#35 / TASK068-C1**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`（下文 TS 行号均指该树）
> 本仓库：`D:\project\w2`（下文 CS 行号均指该树）

---

## 0. 结论（先看这里）

`沙箱` 在本仓库与基准之间不是「实现不同」，而是**四条链路各自断开**：

1. **配置契约不通**：`sandbox` 键（`SettingsJson.cs:283-285`）被映射进 `CodeExecution`（`SettingsMapper.cs:214-229`），字段是 C# 自造的 `mode/image/allowedPaths/restrictNetwork/memoryLimitMb`；基准 schema 是 `enabled/failIfUnavailable/autoAllowBashIfSandboxed/allowUnsandboxedCommands/network{...}/filesystem{...}/excludedCommands/...`（`entrypoints/sandboxTypes.ts:91-144`）。两侧只有 `enabled` 一个词重合，语义还不同。
2. **映射写了没人读**：`SandboxSettings` 映射出的 `ReadOnlyFilesystem / AllowNetworkAccess / MaxMemoryMB / AllowedDirectories` 在 `modules|native_tui|native_gui` 全仓 `rg` 后**没有任何运行时消费方**（只有 `ExecutionTimeoutSeconds` 被 `CodeSandboxMiddleware.cs:31` 读）。即：用户在 settings 里写 `sandbox.enabled=true`，Bash 不会进沙箱，代码执行的内存/网络/只读承诺也不生效 —— 这是**安全语义的假配置**。
3. **Bash 沙箱另起一套**：真正的沙箱是「会话级、手动进入」——`SystemActuatorBase.cs:183-186/230-233` 只看 `_sandboxManager.IsInSandbox`，入口只有 `/sandbox-toggle on`（`SandboxToggleCommand.cs:66`）和 MCP 沙箱工具（`SandboxToolHandlers.cs:68`），与 settings 无连接；进入时还硬编码 `SandboxType.Soft / RestrictNetwork=true / RestrictFileSystem=true`（`SandboxToggleCommand.cs:60-65`）。
4. **权限与逃生阀无联动**：基准 `autoAllowBashIfSandboxed`（默认 true）让沙箱内的 Bash 跳过 ask（`bashPermissions.ts:1349-1361`、`permissions.ts:1186-1200`）；本仓库 `guard.permission` 里 `sandbox` **只出现在 GlobalUsings**（`guard.permission/GlobalUsings.cs:44`）。基准 `allowUnsandboxedCommands` 是策略闸门（`shouldUseSandbox.ts:135-141`），本仓库 `DangerouslyDisableSandbox` 直通 `disableSandbox`（`ShellExecutionMiddleware.cs:35`、`ShellBackgroundMiddleware.cs:38`），**没有策略门**。

> 结论一句话：**基准的沙箱是「配置驱动的每命令安全边界」，本仓库的沙箱是「手动进入的会话状态」；而 settings 里那个 `sandbox` 键是第三套东西，且不生效。**
>
> **进度注**：2026-11-21 起 Phase 1 已修复第 1/3/4 条中的可修复部分（见 §7，方案 B 范围）；第 2 条的 provider 级 per-command 真隔离仍待 Phase 2。

---

## 1. 基准侧证据（TS）

| 事项 | 证据（文件:行号） | 实际行为 |
|------|------------------|----------|
| 配置 schema | `entrypoints/sandboxTypes.ts:91-144` | `enabled`、`failIfUnavailable`、`autoAllowBashIfSandboxed`、`allowUnsandboxedCommands`、`network`、`filesystem`、`ignoreViolations`、`enableWeakerNestedSandbox`、`enableWeakerNetworkIsolation`、`excludedCommands`、`ripgrep`；`passthrough()` 允许 `enabledPlatforms` 等暗门 |
| 网络子 schema | `entrypoints/sandboxTypes.ts:14-42` | `allowedDomains`、`allowManagedDomainsOnly`、`allowUnixSockets`、`allowAllUnixSockets`、`allowLocalBinding`、`httpProxyPort`、`socksProxyPort` |
| 文件系统子 schema | `entrypoints/sandboxTypes.ts:47-86` | `allowWrite`、`denyWrite`、`denyRead`、`allowRead`、`allowManagedReadPathsOnly`（与 Edit/Read 权限规则合并） |
| 设置挂载 | `utils/settings/types.ts:655` | `sandbox: SandboxSettingsSchema().optional()` |
| 默认值 | `utils/sandbox/sandbox-adapter.ts:459-467`、`:469-472`、`:474-477`、`:479-485` | `enabled` 缺省 **false**；`autoAllowBashIfSandboxed` 缺省 **true**；`allowUnsandboxedCommands` 缺省 **true**；`failIfUnavailable` 缺省 false |
| 生效判定 | `utils/sandbox/sandbox-adapter.ts:532-547` | 平台支持 + 依赖齐备 + `enabledPlatforms` 命中 + `enabled=true`，四者同时成立才算「沙箱开着」 |
| 不可用提示 | `utils/sandbox/sandbox-adapter.ts:562-592` | 用户显式开启但跑不起来时给出可操作原因（#34044 的修复：不能静默忽略安全配置） |
| 策略锁 | `utils/sandbox/sandbox-adapter.ts:647-664` | flag/policy 设置覆盖本地时，`/sandbox` 拒绝本地修改 |
| 每命令门控 | `tools/BashTool/shouldUseSandbox.ts:130-153` | `shouldUseSandbox(input)` = 沙箱启用 && （未显式 `dangerouslyDisableSandbox` 或策略不允许）&& 命令不在排除清单 |
| 排除清单 | `tools/BashTool/shouldUseSandbox.ts:21-128` | `excludedCommands` 按 prefix/exact/wildcard 匹配；组合命令逐段匹配；剥离 env 前缀与安全 wrapper 到不动点；企业动态禁用列表另算；注释明说「不是安全边界」 |
| 权限 auto-allow | `tools/BashTool/bashPermissions.ts:1349-1361`、`utils/permissions/permissions.ts:1186-1200` | 沙箱内且 `autoAllowBashIfSandboxed` → 直接 allow（reason 明写）；会跳出沙箱的（排除命令 / `dangerouslyDisableSandbox`）仍走 ask 规则 |
| 提示词 | `tools/BashTool/prompt.ts:166`、`:182-187`、`:228-253` | 按 `allowUnsandboxedCommands` 动态生成：默认留沙箱、何时允许覆盖、策略禁用时「所有命令 MUST 在沙箱内」 |
| 工具参数 | `tools/BashTool/BashTool.tsx:242`、`:813`；`tools/PowerShellTool/PowerShellTool.tsx:743-749` | 参数 `dangerouslyDisableSandbox`；Windows 原生平台沙箱**不支持**（显式置 false） |
| `/sandbox` 命令 | `commands/sandbox-toggle/sandbox-toggle.tsx:14-37`、`:51-71` | 平台/依赖/策略锁检查；无参数进设置菜单；`exclude <pattern>` 写 localSettings 的 `excludedCommands` |
| 包装执行 | `utils/sandbox/sandbox-adapter.ts:172`、`:719`、`:927-970` | `convertToSandboxRuntimeConfig` → `BaseSandboxManager.wrapWithSandbox(...)`，逐命令包裹；接口全部从 `@anthropic-ai/sandbox-runtime` 再导出 |
| 外部依赖 | `package.json:31` | `@anthropic-ai/sandbox-runtime`（srt）——OS 级隔离由该包提供（macOS seatbelt / Linux bwrap+seccomp） |

---

## 2. 本仓库证据（CS）

| 事项 | 证据（文件:行号） | 实际行为 |
|------|------------------|----------|
| settings 字段 | `modules/guard/guard_configuration/core/mapping/SettingsJson.cs:283-285`、`:534-559` | `SandboxSettings` = `enabled/mode/image/allowedPaths/restrictNetwork/memoryLimitMb`（`mode/image` 为自造字段，全仓无消费） |
| 配置映射 | `modules/guard/guard_configuration/core/mapping/SettingsMapper.cs:214-229` | `enabled → CodeExecution.ReadOnlyFilesystem`（**语义错位**：开启沙箱变成只读文件系统）、`restrictNetwork → AllowNetworkAccess` 取反、`memoryLimitMb → MaxMemoryMB`、`allowedPaths → AllowedDirectories` |
| 映射目标 | `modules/abstractions/abstractions_primitives_configuration/src/workflow/WorkflowSubConfigs.cs:3-18` | `CodeExecutionConfig` 的隔离字段（ET/内存/网络/进程/句柄/只读/目录） |
| 实际消费 | `modules/cognition/hands_core/skills/services/code/CodeSandboxMiddleware.cs:31` | **只读** `ExecutionTimeoutSeconds`；其余字段无运行时读取（全仓 `rg` 仅命中映射与测试） |
| `/sandbox` 命令 | `modules/composition/slash/guard/config/SandboxToggleCommand.cs:8`、`:51-73`、`:90-114`、`:145-189` | 运行期进出会话沙箱；硬编码 `Soft` + 双 true；平台判断恒真（Win32NT/Unix/MacOSX 永远「支持」）；`exclude` 写 `sandbox.excludedPaths`（路径，供进入时当 `AllowedPaths`） |
| 会话级生效 | `modules/cognition/hands_shell/system_actuator/abstractions/SystemActuatorBase.cs:183-186`、`:230-233`、`:258-259` | `useSandbox = !disableSandbox && _sandboxManager.IsInSandbox`；未进入沙箱时命令**原样执行**，路径也不重映射 |
| 逃生阀 | `modules/cognition/hands_shell/shell/handlers/middleware/ShellExecutionMiddleware.cs:35`、`ShellBackgroundMiddleware.cs:38`、`shell/handlers/tool_handlers/ShellToolHandlers.cs:72,97` | `DangerouslyDisableSandbox → disableSandbox` 直通，无策略闸门、无审计 |
| 提示词 | `modules/abstractions/ai/prompts/tool_prompts/shell/BashToolPrompt.cs:20` | 仅一行「`dangerously_disable_sandbox` 参数覆盖沙箱模式」，无「默认留沙箱 / 何时可覆盖 / 策略禁止」三段 |
| 沙箱子系统 | `modules/guard/guard_sandbox/core/SandboxManager.cs:47-71`、`modules/abstractions/guard/security/sandbox/core/ISandboxProvider.cs:31`、`SandboxOptions.cs:3-24` | 提供器链（bwrap/docker/process/soft）、profile 排序、provider 级执行能力**都已具备**；`SandboxOptions` 只有粗粒度 `RestrictNetwork/RestrictFileSystem/AllowedPaths/MemoryLimitMb` |
| 权限侧 | `modules/guard/guard_permission/GlobalUsings.cs:44` | 全模块仅此一处出现 sandbox，无 auto-allow、无排除清单联动 |
| 配置键登记 | `modules/abstractions/abstractions_primitives_configuration/src/settings/ConfigKeyRegistry.cs`（无 `sandbox.*` 条目；`:46-47` 表外键回落 `UserSettings` + 扁平 `current.<key>`） | `/sandbox exclude` 写的路径**不是** `SettingsJson.Sandbox.AllowedPaths`（后者走对象路径），两条数据路互不相通 |

---

## 3. 差异清单与影响面

| # | 差异 | 基准 | 本仓库 | 影响 |
|---|------|------|--------|------|
| D1 | 配置契约 | 基准 schema（`enabled/failIfUnavailable/autoAllowBashIfSandboxed/allowUnsandboxedCommands/network/filesystem/excludedCommands/...`） | 自造 6 字段映射到 `CodeExecution` | 用户按基准文档写配置 → 无效；按本仓库字段写 → 只改到没人读的值 |
| D2 | 生效模型 | 每条命令按 settings 决定包裹 | 会话级手动进入，settings 不参与 | 「配了没生效」；同一终端要手动 `/sandbox on`，重启回到未沙箱 |
| D3 | 权限联动 | `autoAllowBashIfSandboxed` 默认 true → 沙箱内 Bash 免 ask | 无 | 沙箱化**没有权限收益**，用户仍逐条确认；沙箱成了纯负担 |
| D4 | 逃生阀 | `allowUnsandboxedCommands` 策略门 + 提示词动态措辞 | 参数直通，无策略门 | 企业无法「强制全沙箱」；模型可无阻力绕开 |
| D5 | 排除清单 | `excludedCommands`（命令模式，逐段匹配，明确非安全边界） | `excludedPaths`（路径，且语义是「沙箱内允许」） | 同名不同物；用户按基准写命令模式 → 不生效 |
| D6 | 提示词 | 三段式动态引导（默认留沙箱/何时覆盖/策略禁止） | 一行参数说明 | 模型不会主动留沙箱，沙箱使用率与安全性都低 |
| D7 | 命令语义 / 平台矩阵 | `/sandbox` = 设置编辑器（策略锁校验）；Windows 原生不支持 | `/sandbox` = 会话进出 + 类型切换；Windows 平台判断恒真 | 语义冲突；Windows 上用户被误导为「已支持」 |

**风险等级**：D1/D2 属**安全语义错配**（配置承诺未兑现），D3/D4 属**策略能力缺失**，D5/D6/D7 属**可用性与一致性**。

---

## 4. 调研前置四问（答完才允许出方案）

### 4.1 专家隐性实践（顶尖沙箱/权限工程师会做、用户没要求的事）

1. **把「安全边界」与「便利功能」分开写清**：基准自己就在 `shouldUseSandbox.ts:18-20` 注明 `excludedCommands` 不是安全边界；任何排除机制都必须同时声明它挡不住什么。
2. **失败模式优先设计**：沙箱不可用时是 fail-open（警告 + 不沙箱）还是 fail-closed（拒绝启动）必须显式可选 —— 基准用 `failIfUnavailable` + 启动原因提示解决 #34044（`sandbox-adapter.ts:549-592`）。
3. **默认值审计**：`enabled=false` / `autoAllow=true` / `allowUnsandboxed=true` 三个默认决定「默认安全还是默认可用」；改动任一默认都要有迁移说明。
4. **逃生阀可审计**：每次 `dangerouslyDisableSandbox` 都应可观测（日志/提示），否则等于没有闸门。
5. **跨平台能力矩阵**：macOS seatbelt / Linux bwrap+seccomp / WSL2 / Windows 原生不支持（`PowerShellTool.tsx:743-749`）；不能用一个 `PlatformID` 判断糊过去。
6. **真实逃逸用例**：写 `/etc`、读 `~/.ssh`、`curl` 外网、git bare-repo 注入（基准 `sandbox-adapter.ts:390-414` 专门 scrub）——只跑单测证明不了边界。
7. **权限与沙箱的「互相不吃」**：沙箱内 auto-allow 必须排除会跳出沙箱的命令，否则沙箱变成权限绕过（`permissions.ts:1186-1200` 的注释就是这条）。
8. **配置单一事实源**：settings 键、`/config set`、`config_set` 工具、`/sandbox` 命令必须读写同一份数据（本仓库已有 `ConfigKeyRegistry` 这个基础设施，本项却没登记）。

### 4.2 原始素材清单（本项从哪些最原始素材入手）

- **基准契约源头**：`src/entrypoints/sandboxTypes.ts`（唯一 schema 真源，注释写明「both SDK and settings validation import from here」）。
- **基准门控源头**：`src/tools/BashTool/shouldUseSandbox.ts`（纯函数，可直接做真值表）、`src/tools/BashTool/bashPermissions.ts`、`src/utils/permissions/permissions.ts`。
- **基准提示词源头**：`src/tools/BashTool/prompt.ts:166-260`。
- **基准命令源头**：`src/commands/sandbox-toggle/sandbox-toggle.tsx`。
- **基准执行源头**：`src/utils/sandbox/sandbox-adapter.ts`（`convertToSandboxRuntimeConfig` / `wrapWithSandbox` / `checkDependencies`）。
- **外部运行时**：`package.json:31` 的 `@anthropic-ai/sandbox-runtime`（本机无 node_modules，只能依 adapter 的调用面与注释还原契约，不能凭空补字段）。
- **本仓库现状**：`SettingsJson.cs`、`SettingsMapper.cs`、`SandboxToggleCommand.cs`、`SystemActuatorBase.cs`、`SandboxManager.cs`、`ISandboxProvider.cs`、`SandboxOptions.cs`、`BashToolPrompt.cs`、`ConfigKeyRegistry.cs`。
- **禁止只看**：TASK068 的历史结论（写于 `guard.sandbox` 落地前，已过期）、任何 AI 总结、本文件自身。

### 4.3 具体方法与工具

1. **双侧 `rg` 逐行对齐**（已完成）：对每个字段做「定义 → 映射 → 消费」三段追踪；判定「无消费方」以 `rg` 在三域全仓零命中为准（不能只看同域）。
2. **真值表测试**：把基准 `shouldUseSandbox` 的 6 个输入维度（enabled / 平台 / 依赖 / dangerous / excluded / 命令空）写成 C# 参数化用例，先红后绿。
3. **真机探针**：settings 写 `sandbox.enabled=true`，用 `JCC_DEBUGLOG=1` 启动 `jcc`，执行越界写命令 + 网络命令，`grep` 打点确认「是否包裹沙箱 / 是否被拒绝」；当前预期是**没有沙箱痕迹**（证明现状），修后预期拒绝。
4. **跨平台**：Linux 用 bwrap 真跑；Windows 用 docker/soft 或按基准声明「不支持」；不允许只在 Windows 单测就宣布对齐。
5. **策略锁演练**：在 policy/flag 设置里写 `sandbox.enabled`，验证 `/sandbox` 拒绝本地修改。

### 4.4 独立验证来源（除 AI 与用户外，什么能证明结果正确）

1. **基准源码本身**：它是行为契约的一手证据（行号已在上表列全）。
2. **真实终端/进程行为**：越界写失败、外网被拒、进程树被清理（strace/`bwrap --unshare` 输出、Windows JobObject 句柄计数），不是 mock。
3. **跨平台 CI 矩阵**：Linux（bwrap/docker 可用）+ Windows（docker 可用 / 明确 unsupported）两条腿都要跑。
4. **既有测试基线**：`Guard.Config.Tests`（映射）、`Guard.Security.Tests`（权限）、`NativeTui.Host.Tests`/`NativeTui.Tests`（提示与接线）、`Composition.Tests`（命令），改动后必须全绿。
5. **产物字符串核验**：`Abstractions.Primitives.Configuration.dll` 含新键名、`Abstractions.Guard.dll` 含新契约类型（UTF-16 检索），防止「只改了源码没进产物」。

---

## 5. 方案（三选一，需用户裁决）

### 方案 A — 完整对齐基准（两阶段）

**Phase 1（契约 + 门控 + 权限 + 逃生阀，安全闭环）**
1. 重写 `SandboxSettings` 为基准 schema（删除 `mode/image/allowedPaths/restrictNetwork/memoryLimitMb` 与 `CodeExecution` 的错误映射；`CodeExecution` 如需保留则改用独立键，不与 `sandbox` 混用）。
2. 新增 `WorkflowConfig.Sandbox`（强类型、可空 default）← `SettingsMapper` 映射；`ConfigKeyRegistry` 登记全部 `sandbox.*` 键（单一事实源）。
3. 新增纯函数 `ShouldUseSandbox(command, dangerousDisable, config)`（对齐基准 6 维判定），接到 Bash/PowerShell 执行与权限管道。
4. `autoAllowBashIfSandboxed` 接入 `PermissionCheckMiddleware`：仅当「确实会沙箱」才跳过 ask。
5. `allowUnsandboxedCommands=false` 时忽略 `dangerouslyDisableSandbox`；`excludedCommands` 生效；`failIfUnavailable` 在启动期硬失败并给出可操作原因。
6. `BashToolPrompt` 三段式动态措辞（对齐 `prompt.ts:166-260`）。
7. `/sandbox` 改为设置语义（写 settings + 策略锁拒绝），保留 `/sandbox-toggle switch` 作为提供器选择；`exclude` 写 `excludedCommands`（命令模式）而不是路径。

**Phase 2（细粒度隔离能力）**
8. 扩展 `SandboxOptions`/provider 映射：`network.allowedDomains`、unix socket、local binding、proxy 端口；`filesystem.allowWrite/denyWrite/denyRead/allowRead`；`ignoreViolations`、`enabledPlatforms`。
9. 平台矩阵：Linux bwrap 真隔离；Windows 仅 docker/soft 或按基准声明 unsupported；启动提示区分「不支持 / 依赖缺失 / 策略关闭」。
10. 企业策略锁（flag/policy 覆盖本地）与审计（逃生阀使用记录）。

- **收益**：真正对齐「配置驱动、每命令、可策略锁定」的基准模型；关掉 D1–D7 全部差异。
- **代价/风险**：跨 4 域 + 提示词；删 legacy 字段属破坏性变更（符合项目「无后向兼容」）；Phase 2 的 bwrap/docker 需跨平台验证；Windows 无法与基准 100% 等价（基准自己也不支持）。
- **适用**：安全优先、企业部署、希望 `/sandbox` 与基准文档一致。

### 方案 B — 最小安全闭环（只做 Phase 1）

- 只补齐「配置→门控→权限→逃生阀→提示词」，细粒度网络/文件清单登记为后续项。
- **收益**：以最小改动消除「假配置」与「沙箱无权限收益」两个真问题；风险可控。
- **代价**：`network.allowedDomains` / `filesystem.denyRead` 等仍不支持，与基准存在**明示**差距。
- **适用**：先止血、再迭代。

### 方案 C — 保留现状 + 防误配

- 不动执行链路；只做：`SettingsEditValidator` 对 `sandbox` 六个字段给出「本仓库不驱动 Bash 沙箱」警告；删除或改名无人消费的映射；总账登记为永久分歧。
- **收益**：零行为风险，成本最低。
- **代价**：配置承诺仍不兑现（只是提前警告）；与基准文档继续分叉。
- **适用**：明确不打算在安全边界上投入。

---

## 6. 验收计划（任何方案都按此口径验收）

| 项 | 方式 |
|----|------|
| 契约 | `SandboxSettings` 反序列化用例（基准字段全谱 + 未知字段容错） |
| 门控 | `ShouldUseSandbox` 真值表：enabled × 平台 × 依赖 × dangerous × excluded × 空命令 |
| 权限 | 沙箱内 auto-allow；排除命令 / 逃生阀仍 ask；策略禁止时 ask 且忽略 dangerous |
| 逃生阀 | `allowUnsandboxedCommands=false` 时 `disableSandbox` 恒 false；打点记录 |
| 失败模式 | 依赖缺失 → 警告 + 不沙箱；`failIfUnavailable=true` → 启动失败 + 原因 |
| 命令语义 | `/sandbox` 写 settings 且策略锁生效；`exclude` 进 `excludedCommands` |
| 真机 | Linux bwrap 真跑越界写/外网/git bare-repo；Windows docker/soft 或 unsupported 提示；`JCC_DEBUGLOG` 打点核验 |
| 回归 | 全仓编译（16 域 + native_tui + native_gui）；`Guard.Config` / `Guard.Security` / `Composition` / `NativeTui.*` 全绿 |
| 产物 | DLL UTF-16 字符串核验（键名 / 契约类型 / 提示文案） |

---

## 7. 实施进度（Phase 1 最小安全闭环）

> 2026-11-21 用户指示「继续这些工作」→ 按 **方案 B（= 方案 A Phase 1）** 推进；Phase 2 细粒度隔离在 Phase 1 验收后另行裁决。

| # | 内容 | 状态 |
|---|------|------|
| P1-1 | `SandboxSettings` 重写为基准 schema（删除 `mode/image/allowedPaths/restrictNetwork/memoryLimitMb`，新增 `network/filesystem/excludedCommands/enabledPlatforms/…`） | ✅ 已完成（编译 + 反序列化用例全谱） |
| P1-2 | `WorkflowConfig.Sandbox`（强类型 `SandboxConfig`）+ `SettingsMapper` 映射 + `ConfigKeyRegistry` 登记 `sandbox.*` 四键 | ✅ 已完成（映射用例：默认值/策略门/不污染 CodeExecution） |
| P1-3 | 纯函数 `ShouldUseSandbox`（基准 6 维真值表）+ 排除清单匹配 | ✅ `workflow/SandboxGate.cs`（15 条真值表：逃生阀策略门/排除前缀·精确·通配/复合命令逐段/环境变量前缀/平台清单） |
| P1-4 | `autoAllowBashIfSandboxed` 接入权限判定（仅「确实会沙箱」才免 ask） | ✅ `SandboxAutoAllowMiddleware`（管道 priority 350；要求 `IsInSandbox` + 提供器 FS∩Network 真隔离；11 条用例） |
| P1-5 | `allowUnsandboxedCommands` 策略门 + `excludedCommands` 生效 + `failIfUnavailable` 启动失败 | ✅ 策略门/排除清单经 `ShellSandboxPolicy` 生效；`failIfUnavailable` 由 `SandboxStartupModule` + `SandboxStartupGate` 落实（不可用→警告或拒绝启动） |
| P1-6 | `BashToolPrompt` 三段式动态措辞（默认留沙箱 / 何时覆盖 / 策略禁止） | ✅ 三段式 + `$TMPDIR` 指引；启动期由 `AddWorkflowServices` 注入；`/sandbox-toggle on/off` 即时同步；4 条用例 |
| P1-7 | `/sandbox` 改设置语义（写 settings + 策略锁）；`exclude` 写 `excludedCommands` | 🟡 `on/off` 已写 `sandbox.enabled` 并同步提示词；`exclude` 改读 `sandbox.excludedCommands`（数组无配置服务写入通道，给可执行指引）；**策略锁（企业 managed 设置覆盖）未做——本仓库无 managed 设置基础设施，单列为独立议题** |
| P1-8 | 配置变更 → 提示词快照热同步（覆盖 config 工具等一切写 `sandbox.*` 的入口） | ✅ `SandboxPromptPolicySync`（composition singleton，订阅 `SettingChanged`，`SandboxStartupModule` 启动期强制实例化；4 条用例） |

### 7.1 实施记录（2026-11-21）

- **门控落点**：`ShellExecutionMiddleware` / `ShellBackgroundMiddleware` 统一走 `ShellSandboxPolicy.ResolveDisableSandbox`；
  `sandbox.enabled=false`（基准缺省）时保持旧行为（仅 `dangerously_disable_sandbox` 生效，不影响手动 `/sandbox` 会话语义），
  `enabled=true` 时由 `SandboxGate` 决定本次命令是否绕过（`[SANDBOX]` 打点记录判定原因）。
- **权限收益**：免确认只在「会话已进沙箱 + 活跃提供器具备 FS/Network 真隔离 + 命令门控判定仍进沙箱」时生效；
  Windows 的 Soft/Process 提供器只有路径重定向/进程隔离，不享受免确认（对齐基准「Windows 不启用 sandbox」）。
- **`/sandbox-toggle exclude` 修正**：原实现写 `sandbox.excludedPaths`（本次 schema 已删除该字段 ⇒ 静默失效）。
  改为读取 `sandbox.excludedCommands` 并提示编辑 settings.json（配置服务只支持标量键，数组写入属独立能力）。
  `/sandbox-toggle on` 的 `AllowedPaths` 改读 `sandbox.filesystem.allowWrite`。
- **`failIfUnavailable` 落点**：新增 `SandboxStartupModule`（engine `[AppModule]`，cli/gui 两个 profile 均登记），
  启动期用 `SandboxStartupGate` 判定「平台白名单 + 真隔离提供器」：不可用且 `failIfUnavailable=true` → 抛错拒绝启动；
  否则 `LogWarning` 明说「命令将不经沙箱 + 如何修复」。真隔离口径统一为 `SandboxManagerCapabilityExtensions`（FS∩Network）。
- **`/sandbox-toggle on/off` 设置语义**：成功进入/退出后写 `sandbox.enabled` 并即时同步 `BashToolPrompt` 快照；
  `status` 增加设置开关一行。
- **提示词热同步（P1-8）**：`SandboxPromptPolicySync`（composition `[Register]` singleton）以启动期 `WorkflowConfig` 为初值，
  订阅 `IConfigurationService.SettingChanged`，命中 `sandbox.enabled` / `sandbox.allowUnsandboxedCommands` 即刷新 `BashToolPrompt` 快照；
  由 `SandboxStartupModule.ConfigureAsync` 首行强制实例化（singleton 随根容器释放退订）。至此 `/sandbox-toggle`、config 工具、
  手工改 settings 三条路径都收敛到同一份提示词策略。
- **未做（下一批）**：策略锁（企业 managed 设置覆盖本地 `/sandbox`，本仓库无 managed 设置载体，需先立基础设施）；
  provider 级 per-command 真实隔离（当前仅会话级，Phase 2）。

---

## 8. 待裁决（Phase 2，Phase 1 验收后再问）

**`network.allowedDomains` / `filesystem.denyRead` 等细粒度隔离、跨平台矩阵、企业策略锁是否进入 Phase 2？**

> 2026-11-21 用户裁决：**全部做**，先记录未做项并按重要性排序，先做已知部分，再扩展未知部分。

---

## 9. 剩余事项总账（2026-11-21，按优先级）

> 口径：**已知** = 基准有明确实现/证据、本仓已有落点或只需补齐消费；**未知** = 缺基础设施或需跨平台真实验证，须先立方案。
> 排序原则：先堵「配置写了但不生效 / 安全收益为空转」的洞，再补平台差异与策略层。

| 序 | 优先级 | 事项 | 类别 | 证据/现状 | 验收方式 |
|----|--------|------|------|-----------|----------|
| R1 | **P0** | `sandbox.enabled=true` 时命令并未真正进沙箱：`SystemActuatorBase` 仅在 `IsInSandbox`（手动 `/sandbox`）时走沙箱，enabled 只影响 `disableSandbox` 开关 | 已知 | 基准 `BashTool.tsx` 用 `SandboxManager.wrapWithSandbox` 逐命令包装；本仓 `SystemActuatorBase.cs:183/230` 只认会话状态 | ✅ 已实现逐命令包装（`SandboxWrapRequest`/`SandboxWrappedCommand`/`ISandboxManager.TryWrapCommandForSandbox` + `BwrapCommandBuilder`），Shell 前台/后台均接线；单测 7 条 + Shell 策略 12 条；全仓编译通过；**Linux 真机待 R9** |
| R2 | **P0** | `autoAllowBashIfSandboxed` 空转：免确认要求 `IsInSandbox`，而 enabled 不会自动进沙箱 ⇒ 永远不触发 | 已知 | `SandboxAutoAllowMiddleware` 已按 `IsInSandbox` 判；依赖 R1 修复后自然成立 | ✅ 免确认条件改为「会话真隔离 或 具备逐命令包装能力」，新增 2 条用例（未进会话但可包装 → 免确认；不可包装 → 回落 ask） |
| R3 | P1 | `filesystem.allowWrite/denyWrite/denyRead/allowRead` 仅契约，未传进 provider | 已知 | `BubblewrapSandboxProvider.ExecuteInSandboxAsync` 只消费 `SandboxInfo.AllowedPaths`；基准 `sandbox-adapter.ts:329-349` 逐项转 runtime config | ✅ 已消费：rw `--bind`（cwd/allowWrite）、ro `--ro-bind`（allowRead/基础系统路径）、掩码 `--tmpfs`/`/dev/null`（denyWrite/denyRead）；allowRead 最后覆盖 denyRead；12 条参数表用例 |
| R4 | P1 | `network.allowedDomains/allowUnixSockets/allowAllUnixSockets/allowLocalBinding/httpProxyPort/socksProxyPort` 未消费 | 已知 | 基准 `sandbox-adapter.ts:359-368`；bwrap 原生只能「开/关网」，域名级需代理 | ✅ 已消费到能力边界：默认 `--unshare-all` 断网；`allowedDomains` 非空 fail-closed + warning；其余 bwrap 不支持项逐个显式 warning（不静默丢弃） |
| R5 | P1 | `EnableWeakerNestedSandbox/EnableWeakerNetworkIsolation/IgnoreViolations/ripgrep` 未消费 | 已知 | 基准 `sandboxTypes.ts:123-141`；其中 weakerNetwork 仅 macOS trustd | ✅ 四项全部进入 `SandboxWrapRequest`：weakerNested/weakerNetwork/ignoreViolations 由 bwrap 显式告警「不支持/已忽略」；`ripgrep` 补齐 `SandboxConfig.Ripgrep`（command/args）→ `SandboxRipgrepSettings` → `SettingsMapper.MapSandboxRipgrep` → `RipgrepCommand` → builder 显式告警「不消费」；映射用例 1 条 + bwrap warning 断言 |
| R6 | P2 | 策略锁：managed/policy settings 覆盖本地 `sandbox.enabled`、`allowManagedDomainsOnly`、`allowManagedReadPathsOnly` | 已知 | `SettingSource.PolicySettings`、`SettingsLoader.LoadPolicySettingsAsync`、`GetManagedSettingsPath` 已存在；缺 sandbox 消费 policy 源与来源感知 | ✅ `SandboxPolicyResolver` 启动期单独投影 policy：三键显式即 `SandboxPolicyConfig.Locked`；managed 域名/读清单只取 policy 源；`/sandbox` 命中锁拒绝本地修改；`SandboxPolicyResolverTests` + `ConfigLoaderTests` managed fixture |
| R7 | P2 | 跨平台 provider 矩阵：macOS Seatbelt（基准 sandbox-runtime）、Windows 无真隔离的显式降级 | **半已知** | 本仓仅 Bubblewrap/Docker/Process/Soft；Windows 判断恒真 | ✅ Windows/Linux 真值：`SandboxPlatformSupport`（Linux 支持、Windows 不支持、macOS Seatbelt 未实现）；`SandboxGate` 先判原生再判白名单；状态/启动警告展示原因；🟡 macOS Seatbelt 经 ADR 0161 明确不支持，待真机另立项 |
| R8 | P2 | `/sandbox` 子命令全对齐（benchmark 的 `/sandbox` 参数矩阵） | 半已知 | 本仓已有 on/off/exclude/status/switch | ✅ 无参数改为设置面板（保留 status 详情）；新增 `help/--help`、未知子命令提示、`exclude` 引号/空模式校验；25 条命令用例 |
| R9 | P1 | 真机 bwrap 验收（当前仅 Windows 本机单测） | 已知但受环境限制 | 云主机 Linux 可用 | 🟢 云主机（Ubuntu，bwrap 0.6.1）实测：`--unshare-all` 下网络被阻断、allowWrite 可写、`--tmpfs` denyRead 掩码生效；发现容器 seccomp 拒绝 `--proc /proc`，已从包装参数移除并注释（沙箱内不暴露 /proc） |
| R10 | P1 | 文档收口：TASK103 #35 状态、TASK104 进度与本总账同步 | 已知 | 本文件 §7/§9 | 文档评审 |

**执行顺序**：R1 → R2（回归）→ R3 → R4 → R5 → R9 → R10 → R6 → R7 → R8。
R1/R2 是「安全收益是否真实」的分水岭，未完成前不宣称 sandbox 已生效。

### 9.1 R1/R2 实施记录（2026-11-21）

- **契约**：`Abstractions.Guard` 新增 `SandboxWrapRequest` / `SandboxWrappedCommand`；`ISandboxProvider` 增
  `SupportsCommandWrapping` + `TryWrapCommand(out ...)`（默认不支持，避免会话型 provider 假装支持）；
  `ISandboxManager` 增 `CanWrapCommand` + `TryWrapCommandForSandbox(out ...)`（符合 JCC11005 TryGet 规范）。
- **包装器**：`BwrapCommandBuilder` 输出一次性 `bwrap --unshare-all --die-with-parent … -- /bin/sh -c <cmd>`；
  绑定工作目录与 allowWrite（rw）、allowRead（ro）、denyWrite/denyRead（tmpfs 或 `/dev/null` 掩码），
  最后再覆盖 allowRead（schema 要求 allowRead 优先于 denyRead）。所有参数按单引号转义，适配 Bash actuator 的 `eval` 链。
- **门控**：`ShellSandboxPolicy.Resolve` 返回 `ShellSandboxDecision`（是否绕过 + 包装命令 + 降级告警）；
  前台/后台中间件都使用包装后的命令。已手动进入会话沙箱时不重复包装。
- **权限**：`SandboxAutoAllowMiddleware` 不再要求 `IsInSandbox`，改为「会话真隔离 或 管理器具备逐命令包装能力」，
  避免 `sandbox.enabled=true` 下免确认永远空转。
- **已知边界**：bwrap 域名级过滤不支持（`allowedDomains` 非空 → fail-closed 断网 + warning）；
  Windows 无真隔离 provider 时门控按 unavailable 回落不沙箱（启动期已有 Warn/Fail 门控）。
- **R9 真机结论（云主机 Ubuntu / bwrap 0.6.1）**：网络在 `--unshare-all` 后不可达；工作目录与 allowWrite 可写；
  `--tmpfs` 掩码使 denyRead 路径不可读；容器 seccomp 拒绝 `--proc /proc`（导致 bwrap 整体退出），
  已移除 proc 挂载并加注释；`--dev /dev` 在该环境可用，保留以支撑 deny 规则的 `/dev/null` 掩码。
- **R5 收尾（ripgrep 契约）**：`SandboxConfig.Ripgrep`（`SandboxRipgrepConfig`：required `Command` + `Args`）→
  `SandboxRipgrepSettings`（`command`/`args`）→ `SettingsMapper.MapSandboxRipgrep`（空 command 保留默认）→
  `SandboxWrapRequest.RipgrepCommand` → `BwrapCommandBuilder` 显式 warning「bwrap 包装不消费 sandbox.ripgrep」。
  这样「配置不丢 + 能力缺口可见」，不做静默吞字段。weakerNested / weakerNetwork / ignoreViolations 同样只告警不假装支持。
- **R10 收口**：本表 R1–R5/R9 状态与 §9.1 记录同时更新；TASK103 #35 指向本节，不重复维护两份进度。
- **R6 落地（2026-11-22 接手）**：`SandboxPolicyResolver` 在 `ConfigLoader` 启动期单独读 `managed-settings.json`，
  把 `SandboxPolicyConfig`（Locked/LockedKeys/AllowManagedDomainsOnly/AllowManagedReadPathsOnly）投影进 `WorkflowConfig`；
  policy 声明 `enabled/autoAllowBashIfSandboxed/allowUnsandboxedCommands` 即锁 `/sandbox`；两个 `allowManaged*Only` 只在 policy 源生效，
  无 policy 时清除用户侧伪造开关。验收：`SandboxPolicyResolverTests` 5 条 + `ConfigLoaderTests` managed fixture。
- **R7 落地（2026-11-22 接手）**：`SandboxPlatformSupport` 区分「原生实现」与 `enabledPlatforms` 白名单 —
  Linux 支持（bwrap 需安装）、Windows 不支持、macOS Seatbelt 未实现；`SandboxGate` 先判原生再判白名单，
  `/sandbox status` 与启动警告展示原因；测试用显式平台参数覆盖（Windows 不再因白名单为真）。
- **R8 落地（2026-11-22 接手）**：`/sandbox` 无参数改为设置面板（保留 `status` 详情），新增 `help/--help`、
  未知子命令提示、`exclude` 引号/空模式校验；`on/off/switch <type>/exclude <pattern>` 命中策略锁一律拒绝本地修改。
  验收：`SandboxToggleCommandTest` 25 条全绿。

---

<!-- 🤖 Auto Decision: 2026-11-21 -->
<!-- 决策: Phase 1 先落「契约 + 门控 + 权限 + 逃生阀 + 提示词」，不做 provider 级细粒度隔离 -->
<!-- 原因: 细粒度网络/文件清单需要跨平台真跑验证（bwrap/docker），风险与工作量都远大于契约层；先止血「假配置 + 无权限收益」 -->
<!-- 替代方案: 一次性做完整对齐（方案 A 全量，未采用：破坏面跨 4 域，须分批验证） -->
<!-- 验证: 契约层已编译 + Guard.Config 用例通过；门控层待真值表红绿 -->

---

## 10. 工作交接（2026-11-22）

> **文档编号：TASK104**（本文件即交接书；接手人从 §10 读起即可，无需重读全部取证）
> **交接分支**：`w2`；接手前 PR #715 已 squash 合并，HEAD `5d182607e`（`origin/main`、`origin/w2` 同点）；本轮 R6/R7/R8 改动在工作树，待提交为新 PR。
> **交接结论**：Phase 1 与 R1–R5 / R9 / R10 **已完成并验证**；R6 / R7（除 Seatbelt）/ R8 **本轮已完成**（2026-11-22 接手），见 §9 与 §11。

### 10.1 一分钟摘要

- **修掉的病**：`sandbox.enabled=true` 原先只改门控布尔值，命令不会真的进沙箱，`autoAllowBashIfSandboxed` 因永远不满足 `IsInSandbox` 而空转 —— 即「配置写了不生效 + 安全收益为零」。
- **现在的行为**：Linux 上 `enabled=true` 时每条 Bash 命令用一次性 `bwrap` 包裹；文件系统 allow/deny 清单、网络开关、工作目录绑定真实生效；真隔离 + 免确认联动；提示词随配置热更新；提供器不可用且在 `failIfUnavailable=true` 时启动即失败。
- **没做的**：macOS Seatbelt（需真机与独立 ADR；`SandboxPlatformSupport` 已明确报「未实现」，不冒充支持）。R6/R7 平台真值/R8 于 2026-11-22 接手后已补齐。
- **评审注意**：`3cd4dded0` 已随 PR #715 合并进 `main`，本轮 PR 不再包含它；PR 描述无需重复。

### 10.2 已完成清单（可直接验收）

| 项 | 落点 | 验收证据 |
|----|------|----------|
| Phase 1 契约 | `SandboxSettings` 基准 schema、`WorkflowConfig.Sandbox`、`ConfigKeyRegistry` 四键 | `SettingsJsonDeserializationTests` / `SettingsMapperTests` 全绿 |
| Phase 1 门控 | `SandboxGate` 六维真值表（enabled/平台/排除/逃生阀/复合命令/env 前缀） | `SandboxGateTests` 全绿 |
| Phase 1 启动门 | `SandboxStartupGate` + `SandboxStartupModule`（不可用 → Warn 或拒绝启动） | `SandboxStartupGateTests` 全绿；cli/gui profile 均登记 |
| Phase 1 提示词 | `BashToolPrompt` 三段式（默认留沙箱 / 何时覆盖 / 策略禁止） | `BashToolPromptSandboxTests` 全绿 |
| Phase 1 热同步 | `SandboxPromptPolicySync` 订阅 `SettingChanged`，配置工具/手改 settings 也会刷新提示词 | `SandboxPromptPolicySyncTest` 4 条全绿 |
| **R1** 逐命令包装 | `ISandboxManager.TryWrapCommandForSandbox` + `BwrapCommandBuilder`；前台/后台 Shell 中间件都改用包装命令 | `BwrapCommandBuilderTest` 12 条；`ShellSandboxPolicyTest` 12 条 |
| **R2** 免确认 | `SandboxAutoAllowMiddleware`：会话真隔离 **或** 管理器可逐命令包装 → 直接 allow；否则回落 ask | `SandboxAutoAllowMiddlewareTest`（含 2 条新场景） |
| **R3** 文件清单 | rw `--bind`（cwd/allowWrite）、ro `--ro-bind`（allowRead/系统路径）、deny 目录 `--tmpfs`、deny 文件 `/dev/null`；allowRead 最后覆盖 denyRead | bwrap 参数表用例 + 云主机手工实测 |
| **R4** 网络清单 | 默认 `--unshare-all` 断网；`restrictNetwork=false` 才 `--share-net`；域名过滤不支持 → fail-closed 断网 + warning；其余 socket/代理字段逐个 warning | bwrap warning 断言 + 云主机 `getent hosts` 实测无解析 |
| **R5** 剩余开关 | weakerNested / weakerNetwork / ignoreViolations 进请求并显式告警；`sandbox.ripgrep` 补齐 command/args 契约 → `RipgrepCommand` → 告警「bwrap 不消费」 | 映射用例 + bwrap warning 断言 |
| **R9** 真机 | 云主机 Ubuntu + bwrap 0.6.1：网络阻断、allowWrite 可写回读、denyRead 掩码生效；`--proc /proc` 被容器 seccomp 拒绝已移除 | §9.1 记录；**注意无 `dotnet`、无仓库，只到参数级真机** |
| **R10** 文档 | 本文件 §9 总账 + §9.1 记录 + 本节交接 | 本提交 |

**最新验证（交接前）**：`pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -NoAnalyzers` 全通过（solution / native_tui / native_gui 全 True）；`Guard.Config.Tests` 全 37 条通过；`BwrapCommandBuilderTest` 12 条通过；Shell / Composition 相关用例在 R1–R3 阶段全绿。

### 10.3 关键代码地图（改哪里、别改哪里）

| 层 | 文件 | 说明 |
|----|------|------|
| 配置契约 | `modules/abstractions/abstractions_configuration/workflow/SandboxConfig.cs` | `SandboxConfig` + `SandboxRipgrepConfig`；新增字段先加这里 |
| 配置文本 | `modules/guard/guard_configuration/core/mapping/SettingsJson.cs` | `SandboxSettings` / `SandboxNetworkSettings` / `SandboxFilesystemSettings` / `SandboxRipgrepSettings` |
| 映射 | `modules/guard/guard_configuration/core/mapping/SettingsMapper.cs` | `MapSandbox*`；**只有这里做 settings→强类型**，勿在消费方解析 JSON |
| 配置源 | `modules/guard/guard_configuration/core/loading/core/SettingsLoader.cs` | 5 源合并已存在（User→Project→Local→Flag→Policy）；`LoadPolicySettingsAsync` / `GetManagedSettingsPath` 可直接用 |
| 门控 | `.../abstractions.configuration/workflow/SandboxGate.cs` | 纯函数真值表；不碰 I/O |
| 启动门 | `.../workflow/SandboxStartupGate.cs` + `modules/apps/engine/app/modules/SandboxStartupModule.cs` | 启动期判平台/真隔离/`failIfUnavailable` |
| 包装契约 | `modules/abstractions/guard/security/sandbox/core/SandboxWrapRequest.cs`、`SandboxWrappedCommand.cs` | `SandboxWrapRequest` 是唯一「一条命令要什么隔离」的描述 |
| 管理器 | `modules/guard/guard_sandbox/core/SandboxManager.cs` | `SelectWrappingProvider`：活跃真隔离 → bwrap → docker（若支持）→ 遍历 |
| bwrap | `modules/guard/guard_sandbox/providers/BwrapCommandBuilder.cs`、`BubblewrapSandboxProvider.cs` | 所有参数/告警在此；**没有 `--proc /proc`，别加回** |
| Shell 接线 | `modules/cognition/hands_shell/shell/handlers/middleware/ShellSandboxPolicy.cs` + `ShellExecutionMiddleware.cs` + `ShellBackgroundMiddleware.cs` | 前台/后台共用同一决策；手动已进会话沙箱时不重复包装 |
| 权限 | `modules/guard/guard_permission/policy/SandboxAutoAllowMiddleware.cs` | 免确认只给「真隔离或可包装」；排除命令/逃生阀仍 ask |
| 命令 | `modules/composition/slash/guard/config/SandboxToggleCommand.cs` | `on/off/status/exclude/switch`；`on/off` 写 `sandbox.enabled` |
| 提示词 | `modules/abstractions/ai/prompts/tool_prompts/shell/BashToolPrompt.cs` | 三段式快照；`SandboxPromptPolicySync` 热更新 |

**不要改的边界**：

1. `SandboxWrapRequest` 字段一旦新增，必须同时接 `ShellSandboxPolicy.BuildWrapRequest` + `BwrapCommandBuilder` 的「不支持就 warning」分支，禁止静默吞字段。
2. 真隔离口径固定为 `SandboxCapabilities.FileSystemIsolation ∩ NetworkIsolation`（`SandboxManagerCapabilityExtensions`）；Windows Soft/Process 不得享受免确认。
3. bwrap 参数里 `--unshare-all` 是默认；只有 `RestrictNetwork=false` 才可 `--share-net`。`allowedDomains` 非空但无法域名过滤时必须 **fail-closed**，不能放行全网。
4. `sandbox.enabled=false`（基准默认）时保持旧行为：只有 `dangerously_disable_sandbox` 影响本次命令；手动 `/sandbox on` 的会话语义照旧。
5. 配置数组（`excludedCommands` / `allowWrite` 等）没有通过 `IConfigurationService` 写入的通道，`/sandbox exclude` 目前只读+指引编辑 settings.json；不要假装能写数组。

### 10.4 剩余事项（接手前快照；R6/R7/R8 已于 2026-11-22 完成）

> 本节保留接手时的差异分析作为追溯记录，不再作为待办清单；完成证据见 §9 总账与 §11。

| 序 | 优先级 | 事项 | 现状与关键判断 | 建议做法 / 验收 |
|----|--------|------|----------------|------------------|
| **R6** | P1（先做） | 企业策略锁 + managed 源限定 | 原先注记「无托管基础设施」**不准确**：`SettingSource.PolicySettings`、`SettingsLoader.LoadPolicySettingsAsync`、`GetManagedSettingsPath`、hooks 的 managed 源都已存在；缺的是 **sandbox 消费 policy 源** 与 **来源感知**（`LoadAllSourcesAsync` 合并后已丢失「哪个键来自 policy」） | ① 新增 `SandboxPolicyLock`（启动期单独 `LoadPolicySettingsAsync`，只判 `sandbox.enabled/autoAllowBashIfSandboxed/allowUnsandboxedCommands` 是否显式存在）；② `/sandbox on/off/switch/exclude` 命中锁 → 拒绝本地修改并说明「被策略覆盖」；③ `allowManagedDomainsOnly=true` → `allowedDomains` 只取 policy 源；④ `allowManagedReadPathsOnly=true` → `allowRead` 只取 policy 源（基准 `sandbox-adapter.ts:152-164`、`:181-196`、`:343-347`）；⑤ 用例：managed-settings fixture + 本地改被拒 + 域/读清单只认 policy |
| **R7** | P2 | 平台矩阵：Windows 显式不支持 / macOS Seatbelt | 本仓 provider 只有 bwrap/docker/process/soft，**没有 Seatbelt**；且 `SandboxToggleCommand.cs:150-152` 的平台判断对 Win32NT/Unix/MacOSX 恒真，会误导 Windows 用户「已支持」 | **先做已知部分**：新增 `SandboxPlatformSupport`（Windows=不支持、Linux=bwrap 可用性检查、macOS=Seatbelt 未实现→不支持），替换恒真判断，`/sandbox status` 明确显示原因；**Seatbelt 属未知项**：需 macOS 真机 + 先写 ADR（provider 契约是否复用 `TryWrapCommand`、sandbox-exec profile 生成、路径 deny 语义），无真机不得宣称支持 |
| **R8** | P2 | `/sandbox` 参数面完整对齐 | 基准 `/sandbox`：无参数 → 交互式设置菜单；只有 `exclude <pattern>` 子命令；本仓：无参数默认 `status`，另有 `on/off/switch` | 先决策「保留本仓扩展还是收敛到基准语义」（建议保留扩展但补：无参数菜单/设置项展示、`exclude` 参数校验与写回指引、各子命令 `--help` 文案）；逐条用例覆盖 `on/off/status/exclude/switch/未知子命令/缺参数`；不要为了对齐删掉现有可用命令 |

**推荐顺序**：R6 → R7（先 Windows 真值，Seatbelt 单独立项）→ R8。R6 是唯一还带「安全承诺」性质的缺口；R7/R8 更偏平台正确性与可用性。

### 10.5 接手后的第一批命令（照抄即可）

```powershell
cd D:\project\w2
git --no-pager status --short
git --no-pager log --oneline -8
pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -NoAnalyzers   # 日志写 .xxx\logs\
dotnet test modules\guard\guard_config_tests\Guard.Config.Tests.csproj --no-build --filter "FullyQualifiedName~SettingsMapperTests"
dotnet test modules\guard\guard_security_tests\Guard.Security.Tests.csproj --no-build --filter "FullyQualifiedName~BwrapCommandBuilderTest|FullyQualifiedName~SandboxAutoAllowMiddlewareTest"
dotnet test modules\cognition\hands_shell_tests\Hands.Shell.Tests.csproj --no-build --filter "FullyQualifiedName~ShellSandboxPolicyTest"
```

- **Linux 真机复验**：`manage_environment(action="switch", env_id="vm")`（Ubuntu，bwrap 0.6.1 已装；**无 dotnet、无仓库**）。只做参数级验证：
  `bwrap --unshare-all --die-with-parent --ro-bind /usr /usr --ro-bind /bin /bin --ro-bind /lib /lib --ro-bind /lib64 /lib64 --ro-bind /sbin /sbin --ro-bind /etc /etc --dev /dev --tmpfs /tmp --bind "$PWD" "$PWD" --chdir "$PWD" -- /bin/sh -c 'getent hosts example.com; echo ok'`
  期望：`getent` 无输出、`echo ok` 正常。**不要加 `--proc /proc`**（容器 seccomp 会 `Operation not permitted`，整条命令失败）。
- **Windows 本机**：bwrap 不存在，`SandboxManager` 会回落 soft/process；这不是 bug，不要为了测试去装 WSL 或改 provider 顺序。

### 10.6 已知坑与排错线索

| 现象 | 根因 | 处理 |
|------|------|------|
| bwrap 整条命令失败、报 `/newroot/proc` | 容器 seccomp 拒绝 `--proc /proc` | 已移除；不要加回，除非目标环境明确允许 mount proc |
| `sandbox.enabled=true` 但命令仍未沙箱 | 提供器无 bwrap / `SupportsCommandWrapping=false` / 平台不在白名单 | 看启动期 `SandboxStartupGate` 日志 + `ShellSandboxPolicy` 的 `[SANDBOX]` 打点；Windows 属预期不支持 |
| 免确认没生效 | 提供器不是「FS∩Network 真隔离」且不可包装 | `SandboxAutoAllowMiddleware` 会回落 ask；查 `SandboxManager.CanWrapCommand` |
| `allowedDomains` 配了却完全断网 | bwrap 无域名过滤能力，按 fail-closed 处理 | 这是刻意保守；要域名级需引入代理/srt 类运行时（R7/新 ADR），不要擅自改成 `--share-net` |
| `/sandbox exclude` 写了没生效 | 原实现写 `excludedPaths`，schema 已删；现仅读 `excludedCommands` 并提示手改 settings | 数组写入是独立能力；不要用标量 config 服务硬塞 |
| 改了 `SandboxSettings` 但测试编译过、运行不生效 | `SettingsMapper` 未映射 / `SandboxConfig` 无字段 | 按 §10.3 三层顺序补：SettingsJson → SettingsMapper → SandboxConfig → 消费方 |
| 提示词还是旧措辞 | 配置变更未触发 `SettingChanged`（手改文件不会发事件） | 重启进程；或经 `/sandbox on/off`、config 工具写入以触发 `SandboxPromptPolicySync` |

### 10.7 交接 Definition of Done（R6/R7/R8 已满足）

1. R6 / R7（Windows 真值部分）/ R8 各自：先写差异或 ADR（涉及策略源/平台的先立 ADR）→ 红测试 → 实现 → 编译 → 绿测试 → 文档 §9 状态更新 → 提交。
2. 每次跨域契约变更后跑全仓 `Build-AllDomains.ps1 -NoAnalyzers`；不并发跑 `dotnet build/test`。
3. R7 的 Seatbelt 若无 macOS 真机，**只写 ADR + provider 占位声明，不实现假支持**；R6 的 policy 锁必须有 managed-settings fixture 用例，不能只写代码。
4. 任何文件不删除；需要移除的源码移到仓库根 `.xxx/` 并修引用。
5. 每轮结束按项目 AGENTS.md 用 `ask_user`（本环境为 `interaction_ask`）向用户提一个与当前上下文相关的问题，不自行扩大范围。

### 10.8 本次交接提交说明

- 本节记录接手轮次：文档 + R6/R7/R8 生产代码与测试；提交后由 MainAgent 推送分支并向 `main` 发起 PR。
- PR 描述应包含：R1–R5/R9/R10 的既有范围、R6/R7（Windows 真值）/R8 本轮追加范围、验证命令与结果、Seatbelt 明确未实现（ADR 0161）、`3cd4dded0` 属独立历史提交。
- 若 PR CI 报红：按 AGENTS.md「PR 后守 CI」流程，拉失败日志 → 修复 → 本地验证 → 直接 push 同一分支，不 force push、不自行 pull/rebase。

## 11. R6/R7/R8 实施记录（2026-11-22 接手）

### 11.1 R6 — 企业策略锁 + managed 源限定

- **投影层**：新增 `SandboxPolicyResolver`（`guard.configuration/core/mapping`），`ConfigLoader` 在启动期单独调用
  `SettingsLoader.LoadPolicySettingsAsync`，把 policy 快照投影为 `WorkflowConfig.SandboxPolicy`：
  - `sandbox.enabled` / `sandbox.autoAllowBashIfSandboxed` / `sandbox.allowUnsandboxedCommands` 任一显式存在 → `Locked=true` 并记录 `LockedKeys`；
  - `network.allowManagedDomainsOnly=true` → `Sandbox.Network.AllowedDomains` 只保留 policy 源；
  - `filesystem.allowManagedReadPathsOnly=true` → `Sandbox.Filesystem.AllowRead` 只保留 policy 源；
  - 无 policy 时把两个 managed 开关强制置 false，用户/项目侧伪造无效。
- **命令消费**：`SandboxToggleCommand` 对 `on/enable`、`off/disable`、`switch <type>`、`exclude <pattern>` 在确认与执行前拒绝；
  `EnableSandboxAsync` / `DisableSandboxAsync` / `PersistSandboxEnabledAsync` 各自再做一次守卫，防内部直接调用绕过。
- **文件**：`SandboxConfig.cs`（`SandboxPolicyConfig`）、`WorkflowConfig.cs`、`SandboxPolicyResolver.cs`、
  `ConfigLoader.cs`、`SandboxToggleCommand.cs`。
- **测试**：`SandboxPolicyResolverTests` 5 条（三键锁、network 不锁、托管域名、托管读路径、无 policy 伪造失效）；
  `ConfigLoaderTests.LoadConfig_ManagedSettings_AppliesPolicyLockAndManagedLists` 用临时 `managed-settings.json` 做端到端断言。

### 11.2 R7 — 平台真值矩阵

- 新增 `SandboxPlatformSupport`：Linux 支持（bubblewrap，需安装）、Windows 不支持、macOS Seatbelt 未实现、未知平台不支持；`Reason` 直接供状态/启动警告使用。
- `SandboxGate.IsPlatformSupported` 改为「先判原生实现，再判 `enabledPlatforms` 白名单」：Windows/macOS 即使白名单包含也返回 false。
- `SandboxStartupGate.Evaluate` 增加可选 `platform` 参数并区分原因：
  原生不支持 → 平台原因；原生支持但白名单不含 → `enabledPlatforms`；平台/提供器都可用但无真隔离 → 提供器原因。
- `ShellSandboxPolicy` 与 `SandboxAutoAllowMiddleware` 增加可选平台参数（生产默认当前平台，测试显式指定）。
- macOS 按决策**不实现假支持**：仅显式报缺口，后续需 macOS 真机 + 独立 ADR 才能接 Seatbelt。

### 11.3 R8 — `/sandbox` 参数面

- 无参数 → `ShowSettingsMenu`：先展示 `status`，再展示 `current.sandbox` 的 `enabled/failIfUnavailable/autoAllowBashIfSandboxed/allowUnsandboxedCommands/excludedCommands/network.allowedDomains/filesystem.allowWrite/allowRead`。
- 新增 `help` / `--help` / `-h`，含无参数、status、on/off、exclude、switch 与示例；未知子命令输出错误后展示帮助。
- `exclude` 模式参数做首尾引号裁剪；裁剪后为空给用法提示；仍保持只读 + 指引编辑 settings.json。
- `status` 增加策略锁行与平台说明行，平台支持判断改用 `SandboxPlatformSupport`，不再用恒真的 `PlatformID`。

### 11.4 验证结果（本机 Windows，2026-11-22）

| 验证 | 命令 | 结果 |
|------|------|------|
| 全仓编译 | `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -NoAnalyzers` | 全部通过（solution / native_tui / native_gui ×2） |
| Guard.Config 全量 | `dotnet test modules\guard\guard_config_tests\Guard.Config.Tests.csproj --no-build` | 1354 通过 / 0 失败 |
| Guard.Security 全量 | `dotnet test modules\guard\guard_security_tests\Guard.Security.Tests.csproj --no-build` | 1256 通过 / 0 失败 |
| Hands.Shell 全量 | `dotnet test modules\cognition\hands_shell_tests\Hands.Shell.Tests.csproj --no-build` | 541 通过 / 0 失败 |
| Composition 全量 | `dotnet test modules\composition\composition_tests\Composition.Tests.csproj --no-build` | 1144 通过 / 0 失败 |

日志在 `.xxx/logs/`：`build_all_r6r7r8_final.txt`、`test_config_full.txt`、`test_security_full.txt`、`test_shell_full.txt`、`test_composition_full.txt`。

### 11.5 已知边界

- macOS 无真机：Seatbelt 仅 `SandboxPlatformSupport` 报「未实现」，不进入 provider 选择链；接 Seatbelt 另立 ADR。
- `ConfigLoader` 未接入 flag settings 路径：本轮策略锁只消费 `managed-settings.json`，flag 锁待后续接入（ADR 0161 已注明）。
- Windows 本机只做单测与编译验证；`/sandbox status` 的人工交互输出未在真实终端逐行核对，命令测试覆盖了文案关键行。
