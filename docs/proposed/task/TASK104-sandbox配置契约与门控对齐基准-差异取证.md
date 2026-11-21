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
| settings 字段 | `modules/guard/guard.configuration/core/mapping/SettingsJson.cs:283-285`、`:534-559` | `SandboxSettings` = `enabled/mode/image/allowedPaths/restrictNetwork/memoryLimitMb`（`mode/image` 为自造字段，全仓无消费） |
| 配置映射 | `modules/guard/guard.configuration/core/mapping/SettingsMapper.cs:214-229` | `enabled → CodeExecution.ReadOnlyFilesystem`（**语义错位**：开启沙箱变成只读文件系统）、`restrictNetwork → AllowNetworkAccess` 取反、`memoryLimitMb → MaxMemoryMB`、`allowedPaths → AllowedDirectories` |
| 映射目标 | `modules/abstractions/abstractions.primitives.configuration/src/workflow/WorkflowSubConfigs.cs:3-18` | `CodeExecutionConfig` 的隔离字段（ET/内存/网络/进程/句柄/只读/目录） |
| 实际消费 | `modules/cognition/hands_core/skills/services/code/CodeSandboxMiddleware.cs:31` | **只读** `ExecutionTimeoutSeconds`；其余字段无运行时读取（全仓 `rg` 仅命中映射与测试） |
| `/sandbox` 命令 | `modules/composition/slash/guard/config/SandboxToggleCommand.cs:8`、`:51-73`、`:90-114`、`:145-189` | 运行期进出会话沙箱；硬编码 `Soft` + 双 true；平台判断恒真（Win32NT/Unix/MacOSX 永远「支持」）；`exclude` 写 `sandbox.excludedPaths`（路径，供进入时当 `AllowedPaths`） |
| 会话级生效 | `modules/cognition/hands_shell/system_actuator/abstractions/SystemActuatorBase.cs:183-186`、`:230-233`、`:258-259` | `useSandbox = !disableSandbox && _sandboxManager.IsInSandbox`；未进入沙箱时命令**原样执行**，路径也不重映射 |
| 逃生阀 | `modules/cognition/hands_shell/shell/handlers/middleware/ShellExecutionMiddleware.cs:35`、`ShellBackgroundMiddleware.cs:38`、`shell/handlers/tool_handlers/ShellToolHandlers.cs:72,97` | `DangerouslyDisableSandbox → disableSandbox` 直通，无策略闸门、无审计 |
| 提示词 | `modules/abstractions/ai/prompts/tool_prompts/shell/BashToolPrompt.cs:20` | 仅一行「`dangerously_disable_sandbox` 参数覆盖沙箱模式」，无「默认留沙箱 / 何时可覆盖 / 策略禁止」三段 |
| 沙箱子系统 | `modules/guard/guard.sandbox/core/SandboxManager.cs:47-71`、`modules/abstractions/guard/security/sandbox/core/ISandboxProvider.cs:31`、`SandboxOptions.cs:3-24` | 提供器链（bwrap/docker/process/soft）、profile 排序、provider 级执行能力**都已具备**；`SandboxOptions` 只有粗粒度 `RestrictNetwork/RestrictFileSystem/AllowedPaths/MemoryLimitMb` |
| 权限侧 | `modules/guard/guard.permission/GlobalUsings.cs:44` | 全模块仅此一处出现 sandbox，无 auto-allow、无排除清单联动 |
| 配置键登记 | `modules/abstractions/abstractions.primitives.configuration/src/settings/ConfigKeyRegistry.cs`（无 `sandbox.*` 条目；`:46-47` 表外键回落 `UserSettings` + 扁平 `current.<key>`） | `/sandbox exclude` 写的路径**不是** `SettingsJson.Sandbox.AllowedPaths`（后者走对象路径），两条数据路互不相通 |

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
| P1-7 | `/sandbox` 改设置语义（写 settings + 策略锁）；`exclude` 写 `excludedCommands` | 🟡 `on/off` 已写 `sandbox.enabled` 并同步提示词；`exclude` 改读 `sandbox.excludedCommands`（数组无配置服务写入通道，给可执行指引）；**策略锁（企业 managed 设置覆盖）未做** |

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
- **未做（下一批）**：策略锁（企业 managed 设置覆盖本地 `/sandbox`）；provider 级 per-command 真实隔离（当前仅会话级，Phase 2）；
  通过 config 工具改 `sandbox.enabled` 后的提示词快照同步（目前只有 `/sandbox-toggle` 路径即时同步）。

---

## 8. 待裁决（Phase 2，Phase 1 验收后再问）

**`network.allowedDomains` / `filesystem.denyRead` 等细粒度隔离、跨平台矩阵、企业策略锁是否进入 Phase 2？**

---

<!-- 🤖 Auto Decision: 2026-11-21 -->
<!-- 决策: Phase 1 先落「契约 + 门控 + 权限 + 逃生阀 + 提示词」，不做 provider 级细粒度隔离 -->
<!-- 原因: 细粒度网络/文件清单需要跨平台真跑验证（bwrap/docker），风险与工作量都远大于契约层；先止血「假配置 + 无权限收益」 -->
<!-- 替代方案: 一次性做完整对齐（方案 A 全量，未采用：破坏面跨 4 域，须分批验证） -->
<!-- 验证: 契约层已编译 + Guard.Config 用例通过；门控层待真值表红绿 -->

