# 0161. Sandbox 策略源与平台真值 — managed 投影与 Seatbelt 缺口显式化

- 状态：accepted（已实现，TASK104 R6/R7）
- 日期：2026-11-22
- 决策者：项目架构组

## 背景

TASK104 完成 Phase 1 与逐命令包装后，仍有两个结构性缺口：

1. **策略源不可见**：`SettingsLoader.LoadAllSourcesAsync` 按 User→Project→Local→Flag→Policy 合并，合并后丢失「哪个键来自 managed-settings」的来源信息。基准的 `areSandboxSettingsLockedByPolicy()` 要求：policy/flag 显式定义 `sandbox.enabled` / `autoAllowBashIfSandboxed` / `allowUnsandboxedCommands` 时，本地 `/sandbox` 必须拒绝修改；`allowManagedDomainsOnly` / `allowManagedReadPathsOnly` 也只在 policy 源生效。本仓此前只能拿到合并值，无法正确实现；且本仓 `ConfigLoader` 未接入 flag settings 路径，故本轮只消费 policy 源，flag 锁待后续接入。
2. **平台真值恒真**：`SandboxToggleCommand` 用 `Win32NT/Unix/MacOSX` 判断平台支持，三者恒真；`SandboxGate` 只看 `enabledPlatforms` 白名单，无法表达「Windows 没有原生沙箱」「macOS Seatbelt 尚未实现」。用户会在 Windows 上看到「平台支持=是」，但命令实际不沙箱。

## 决策

1. **策略投影单独一层**：新增 `SandboxPolicyResolver`（guard.configuration），启动期由 `ConfigLoader` 额外读取 `SettingsLoader.LoadPolicySettingsAsync`，把 policy 快照投影到 `WorkflowConfig`：
   - 仅 `sandbox.enabled` / `autoAllowBashIfSandboxed` / `allowUnsandboxedCommands` 三键显式存在时设置 `SandboxPolicyConfig.Locked`；
   - `network.allowManagedDomainsOnly=true` → 合并后的 `allowedDomains` 替换为 policy 源清单；
   - `filesystem.allowManagedReadPathsOnly=true` → `allowRead` 替换为 policy 源清单；
   - 无 policy 时强制清除用户/项目侧伪造的 managed 开关（这两个开关只在 managed 中生效）。
2. **平台真值单独一层**：新增 `SandboxPlatformSupport`：
   - Linux = 支持（bubblewrap，需本机安装）；
   - Windows = 不支持（对齐基准）；
   - macOS = 不支持（Seatbelt provider 尚未实现），不得以「OS 是 macOS」冒充支持。
   `SandboxGate.IsPlatformSupported` 先判原生实现，再判 `enabledPlatforms` 白名单；`SandboxStartupGate` 与 `/sandbox status` 都展示原因。
3. **`/sandbox` 消费策略锁**：`on/off/switch <type>/exclude <pattern>` 命中锁时拒绝本地修改并给出 managed-settings 键名与下一步；`status/help/无参数` 仍可读。
4. **Seatbelt 不在本 ADR 落地**：仅声明缺口，后续需 macOS 真机 + 独立 ADR（是否复用 `TryWrapCommand`、`sandbox-exec` profile 生成、路径 deny 语义）。
5. **不改既有配置合并顺序**：policy 仍是最高优先级；本次只补来源投影，不重写 `SettingsLoader` 的 5 源合并。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 让 `SettingsLoader.LoadAllSourcesAsync` 返回每键来源 | 需要改所有调用方与合并模型，破坏面远大于一个投影层；当前只有 sandbox 需要来源 |
| 在 `IConfigurationService` 增加「按源读」接口 | 配置服务是标量键读写通道，不承载对象树；会把策略判断散落到命令层 |
| 由 provider 可用性反推平台支持 | 会把「平台不支持」与「依赖未安装」混为一谈，Windows 上无法给出正确修复指引 |
| 直接实现 macOS Seatbelt | 无 macOS 真机，无法验证 sandbox-exec profile 与路径 deny 语义；假支持比明确不支持更危险 |

## 后果

- 正面：`/sandbox` 不再能绕过企业 managed 策略；`allowManaged*Only` 真正只认 policy 源；Windows/macOS 平台状态不再恒真；启动警告与状态面板能给出可操作原因。
- 代价：启动期多读一次 `managed-settings.json`（I/O 很小）；`SandboxConfig` 的 managed 标志必须经投影层解释，不能直接信合并值。
- 风险控制：`SandboxPolicyConfig` 只由 policy 派生；无 policy 时显式清空，避免用户侧伪造；Seatbelt 明确为未实现，不进入 provider 选择链。

## 关联

- 任务文档：`docs/proposed/task/TASK104-sandbox配置契约与门控对齐基准-差异取证.md` §9 R6/R7、§10 交接
- 基准：`utils/sandbox/sandbox-adapter.ts:152-164`（managed 开关）、`:181-196`（域名）、`:343-347`（读路径）、`:647-664`（策略锁）
- 落点：`modules/guard/guard.configuration/core/mapping/SandboxPolicyResolver.cs`、`modules/abstractions/abstractions.configuration/workflow/SandboxPlatformSupport.cs`
- 消费：`ConfigLoader`、`SandboxGate`、`SandboxStartupGate`、`SandboxToggleCommand`
