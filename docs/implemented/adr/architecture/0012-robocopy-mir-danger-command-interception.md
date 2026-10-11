# 0012. robocopy /MIR 与 /PURGE 红色命令拦截

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-09-13

## 背景

### 事件起因

2026-09-13 清理 git worktree 残留目录时，`D:\project\w2\.xxx\nul.20260912.del` 含 Windows 保留设备名 `nul`，`Remove-Item` 和 `[System.IO.File]::Delete` 均失败（Windows 路径解析层把 `nul` 当设备名拦截）。最终用 **robocopy /MIR 空目录覆盖法** 成功删除：robocopy 绕过 Win32 路径解析直接走 NT API，能删除保留名文件。

### 发现的安全缺口

JoinCode 的命令拦截系统（ADR [0047](0047-unified-danger-level-classification.md) 5 级分级 + ADR [0039](0039-command-interception-state-machine.md) 守卫链）已拦截：
- `cmd del` + `\\?\` 长路径前缀（工具安全策略层）
- `rm -rf`、`format c:`、`dd of=/dev/`、`diskpart clean` 等破坏性组合（`DangerousCommandCatalog.Combinations`）

**但 robocopy /MIR 和 /PURGE 未被任何层拦截**，存在以下风险：

| 风险点 | 说明 | 严重度 |
|--------|------|--------|
| **误删生产代码** | 目标路径填错（如 `D:\project\JoinCode`）会清空整个代码库 | 🔴 致命 |
| **不可逆** | /PURGE 删除不进回收站，永久删除，无 undo | 🔴 致命 |
| **绕过保留名保护** | 能删 `nul`/`con`/`aux` 等保留名文件（本是优点，也是危险点） | 🟡 双刃剑 |
| **无确认即时执行** | `/R:0 /W:0` 不重试不等待，瞬间完成 | 🟡 高 |
| **当前无拦截** | `DangerousCommandCatalog` 未登记 `robocopy`，分类器返回 `Unknown`（黄灯），Ask 模式下仅确认即可执行 | 🔴 拦截缺失 |

### 当前 robocopy 的分类路径

```
robocopy 命令
  → DangerousCommandCatalog.Commands 查找 → 未登记
  → CommandDangerClassifier.Classify → 返回 Unknown（黄灯）
  → DangerousCommandProtectionMiddleware → Ask 模式黄灯待确认
  → 用户确认 → 执行
```

问题：黄灯仅"待确认"，用户可能不了解 `/MIR` 的破坏性就确认。且 `/MIR` + 目标路径在工作目录内时，应直接红灯（不可撤回需确认）甚至黑灯（直接拒绝）。

### 根因分析：`nul` 文件的创建链

本次事件的根本原因不是 robocopy，而是 **`nul` 保留名文件被意外创建**。完整链路：

```
AI 在 git bash 中执行含 `> nul` 或 `2>nul` 的命令（Windows cmd 风格的静默输出）
  → MSYS2 bash 不把 nul 当设备名，而是当普通文件名 → 创建了名为 nul 的文件
  → 按 AGENTS.md "移动代替删除" 规则移到 .xxx/nul.20260912.del 归档
  → 删除归档文件时 Windows 路径解析层把 nul 当设备名 → Remove-Item 失败
  → 只能用 robocopy /MIR 绕过 Win32 解析才能删 → 又触发 robocopy /MIR 安全缺口
```

**关键区分**：
- `> nul` **重定向**（如 `echo x > nul`）— 几乎100%是误用（本意丢弃输出但语法用错），合法需求应改用 `> /dev/null`
- `touch nul` **创建**（如 `touch nul`）— 可能是合法需求，不应拦截
- **拦截的核心是重定向符号 `>` + 任意 Windows 保留设备名**（nul/con/prn/aux/com1-9/lpt1-9），不是某个特定文件名 — 只拦"重定向到保留设备名"这个操作，不拦文件名本身
- 不检查特定 `>` + `nul` 组合，而是检查 `>` + **保留设备名集合** — 否则 AI 换成 `> con` 或 `> prn` 就绕过拦截
- 拦截 `> 保留设备名` **重定向**的粒度不大，因为 git bash 中重定向到保留设备名几乎没有合法场景

### 现有实现评估：`BashSystemActuator.RewriteWindowsNullRedirect`

项目已实现 `> nul` 重定向改写逻辑：

**文件**：`core/execution/Hands/src/SystemActuator/Instances/BashSystemActuator.cs:220-236`

| 维度 | 评估 |
|------|------|
| **逻辑** | 把 `>nul`/`2>nul`/`>>nul`/`<nul` 等所有变体改写为 `>/dev/null` |
| **测试** | `BashSystemActuatorNullRedirectTests.cs` 12 个测试，覆盖全面 |
| **JoinCode 自身 agent** | ✅ 凑效 — 命令经过 `BashSystemActuator` 改写后再执行 |
| **CodeArts agent（外部）** | ❌ 不凑效 — 外部 agent 的命令不经过 JoinCode 代码，直接在 MSYS2 bash 中创建 `nul` 文件 |
| **PowerShell/cmd 路径** | ✅ 凑效 — 原生识别 NUL 设备名，不需要改写 |
| **策略** | 自动改写（静默修正） |

**自动改写的问题**：静默修正会掩盖问题 — AI 不知道自己用错了语法，下次还会犯。且无法防止外部 agent（如通过 MCP 连接的其他 AI）的碎碎文字中意外包含 `> nul`。

### 现有实现的 2 个潜在缺口

| # | 缺口 | 文件:行号 | 说明 |
|---|------|-----------|------|
| 1 | `DosDeviceNameRegex` 不匹配裸 `NUL` | `foundation/Abstractions/04-guard/Security/Scanning/SecurityPatterns.cs:489-493` | 正则 `\.(CON\|PRN\|AUX\|NUL\|...)$` 只匹配扩展名位置（如 `foo.NUL`），不匹配裸 `NUL` 作为完整文件名 |
| 2 | `PathConstraintValidator` 白名单放行 `NUL` | `core/safety/Guard/src/Security/PathConstraintValidator.cs:617-622` | 把 `NUL` 与 `/dev/null` 同等视为安全。若改写先执行则无问题，但若有路径绕过改写，`NUL` 重定向会被放行而在 git bash 中创建文件 |

### 同类缺口：工具层拦截但 JoinCode 拦截系统未覆盖的命令

本次事件中，以下命令被 **CodeArts 工具层（bash 工具安全策略）** 拦截，但 **JoinCode 自身的 `DangerousCommandCatalog` / `CommandDangerClassifier` 未覆盖**。JoinCode 作为代码智能体，自身执行命令时也会遇到这些场景，必须一并补登：

| # | 被拦截命令 | 工具层拦截原因 | JoinCode 拦截状态 | 危险本质 |
|---|-----------|---------------|------------------|---------|
| 1 | `cmd /c "del \\?\D:\path\nul"` | 检测到高危命令 | ❌ 未覆盖 | `cmd /c` 间接调用 + `\\?\` 长路径前缀绕过 Win32 解析 + `del` 删除保留名文件 |
| 2 | `Remove-Item -Force '\\?\D:\path\nul'` | 检测到高危路径 | ❌ 未覆盖 | `\\?\` 长路径前缀 + `-Force` 强制删除保留名文件 |

**缺口分析**：

1. **`cmd /c del` 间接调用** — `DangerousCommandCatalog` 登记了 `del`，但 `cmd /c "del ..."` 通过 cmd 间接调用时，分类器只看到 `cmd` 外壳，未解析内层 `del`。`cmd` 本身可能被登记为 Safe/Unknown，导致间接删除绕过拦截。

2. **`\\?\` 长路径前缀** — `DangerousCommandCatalog.DangerousPaths` 登记了 `C:\`、`/`、`..` 等危险路径，但未登记 `\\?\` 前缀。该前缀绕过 Win32 路径解析直接走 NT API，能删除保留名文件（`nul`/`con`/`aux`），是绕过常规删除限制的逃逸路径。

3. **`-Force` 参数** — `DangerousCommandCatalog.Flags` 登记了 `-f`/`-force`，但 PowerShell 的 `-Force`（大小写不敏感）是否被匹配需验证。且 `Remove-Item -Force` + `\\?\` 组合未登记为危险组合。

## 决策

将 `robocopy` 命令及其 `/MIR`、`/PURGE` 参数，以及上述同类缺口命令，一并纳入 `DangerousCommandCatalog` 统一管理，按场景分级拦截。

### 1. 命令定义层（`DangerCommandDefinitions.cs`）

**文件**：`core/safety/Guard/src/Security/DangerClassification/DangerCommandDefinitions.cs`

在 Execution 级（红灯，line 66-169 区域）添加 `robocopy` 命令定义：

```csharp
// robocopy 基础命令：红灯（不可撤回，需确认）
// 理由：robocopy 可覆盖/删除目标文件，/MIR 和 /PURGE 会批量删除
[DangerCommand("robocopy", CommandDangerLevel.Execution)]
```

**为什么是红灯而非黑灯**：robocopy 本身是合法的文件同步工具，常规用法（如 `robocopy src dst /E`）只是复制，不应直接拒绝。危险的是 `/MIR` 和 `/PURGE` 参数，由组合规则升级为黑灯。

### 2. 危险参数层（`DangerousCommandCatalog.Flags.cs`）

**文件**：`core/safety/Guard/src/Security/DangerClassification/DangerousCommandCatalog.Flags.cs`

在 `Flags` 映射表（line 9-42 区域）添加 robocopy 专属危险参数：

```csharp
// robocopy 危险参数：镜像同步会删除目标中不存在于源的文件
["/MIR"]    = CommandDangerLevel.Dangerous,   // /E + /PURGE，镜像覆盖
["/PURGE"]  = CommandDangerLevel.Dangerous,   // 删除目标中多余的文件/目录
```

**为什么参数直接定为黑灯（Dangerous）**：`/MIR` 和 `/PURGE` 的本质是"删除目标中不存在于源的文件"，等价于批量 `rm -rf`，不可撤回、不可逆。无论目标路径在哪，这两个参数都应直接拒绝不提示（黑灯安全红线）。

### 3. 危险组合层（`DangerousCommandCatalog.Flags.cs`）

**文件**：`core/safety/Guard/src/Security/DangerClassification/DangerousCommandCatalog.Flags.cs`

在 `Combinations` 列表（line 44-73 区域）添加显式危险组合，用于审计日志和诊断信息：

```csharp
// robocopy 镜像覆盖组合
new("robocopy", "/MIR",   CommandDangerLevel.Dangerous, "镜像同步会清空目标目录中所有不存在于源的文件，等价批量rm -rf"),
new("robocopy", "/PURGE", CommandDangerLevel.Dangerous, "删除目标目录中所有不存在于源的文件/子目录，不可逆"),
```

### 4. 特殊放行规则（保留名文件清理场景）

**问题**：上述方案会把所有 `robocopy /MIR` 都定为黑灯直接拒绝，但清理 Windows 保留名文件（`nul`/`con`/`prn`）时确实需要 robocopy /MIR（这是唯一可靠的方法）。

**放行方案**：新增 `RobocopyMirrorGuard`（`ICommandGuard` 实现），在守卫链阶段 1 做白名单放行判断：

**文件**：`core/safety/Guard/src/Hooks/Execution/Interception/Guards/RobocopyMirrorGuard.cs`（新增）

```
放行条件（全部满足才放行）：
  1. 命令是 robocopy 且含 /MIR 或 /PURGE
  2. 源目录为空目录（无任何文件和子目录）
  3. 目标目录在工作目录之外（非生产代码区）
  4. 目标目录含 Windows 保留名文件（nul/con/prn/aux/com1-9/lpt1-9）
  5. 用户显式确认（Ask 模式弹出确认，Auto 模式拒绝）

满足 → Allow（放行，降级为红灯确认）
不满足 → 交给 DangerousCommandProtectionMiddleware 按黑灯拒绝
```

**守卫优先级**：高于 `DangerousCommandProtectionMiddleware`，在守卫链阶段 1 先判断是否属于保留名清理场景。

### 5. 拦截后的执行流程（目标状态）

```
robocopy src dst /MIR
  → RobocopyMirrorGuard 检查
    ├─ 保留名清理场景（源空+目标在外+含nul）→ Allow + 降级红灯确认
    │   → DangerousCommandProtectionMiddleware → Ask 模式红灯待确认
    │   → 用户确认 → 执行
    └─ 非保留名场景 → 不放行
        → DangerousCommandCatalog.Flags 查到 /MIR = Dangerous（黑灯）
        → DangerousCommandProtectionMiddleware → 所有模式直接拒绝不提示
```

### 6. 同类缺口拦截（cmd /c 间接调用 + \\?\ 长路径前缀）

针对背景中"工具层拦截但 JoinCode 未覆盖"的命令，补充以下拦截规则：

#### 6.1 `cmd /c` 间接调用解析

**问题**：`cmd /c "del ..."` 通过 cmd 外壳间接调用 `del`，分类器只看到 `cmd`，未解析内层命令。

**方案**：新增 `CmdIndirectCallGuard`（`ICommandGuard` 实现），解析 `cmd /c` / `cmd /k` 内层命令并递归分类。

**文件**：`core/safety/Guard/src/Hooks/Execution/Interception/Guards/CmdIndirectCallGuard.cs`（新增）

```
拦截逻辑：
  1. 检测命令以 cmd /c 或 cmd /k 开头
  2. 提取内层命令字符串（处理引号、转义）
  3. 对内层命令调用 CommandDangerClassifier.Classify 递归分类
  4. 取外层 cmd 分类与内层分类的最高等级作为最终等级
     例：cmd=Unknown(黄) + del=Execution(红) → 最终 Execution(红)
     例：cmd=Unknown(黄) + del + \\?\=Dangerous(黑) → 最终 Dangerous(黑)
```

同理适用于 `powershell -Command "..."` / `pwsh -Command "..."` 间接调用。

#### 6.2 `\\?\` 长路径前缀登记

**问题**：`DangerousCommandCatalog.DangerousPaths` 未登记 `\\?\` 前缀，该前缀绕过 Win32 路径解析直接走 NT API，能删除保留名文件。

**方案**：在 `DangerousCommandCatalog.Flags.cs` 的 `DangerousPaths` 集合（line 75-103 区域）添加：

```csharp
// 长路径前缀：绕过 Win32 路径解析，能删除保留名文件（nul/con/aux）
"\\\\?\\"   // Win32 长路径前缀（\\?\）
"\\\\.\\\\" // Win32 设备命名空间前缀（\\.\）
```

**分类**：含 `\\?\` 或 `\\.\` 前缀的路径 → 至少 Execution（红灯），若同时含删除命令 → Dangerous（黑灯）。

#### 6.3 `Remove-Item -Force` + 长路径组合

**问题**：`Remove-Item -Force '\\?\path'` 组合未登记为危险组合。

**方案**：在 `Combinations` 列表添加：

```csharp
new("Remove-Item", "\\?\\", CommandDangerLevel.Dangerous, "长路径前缀+强制删除，绕过Win32路径解析删除保留名文件"),
new("del",         "\\?\\", CommandDangerLevel.Dangerous, "长路径前缀+del，绕过Win32路径解析删除保留名文件"),
```

### 7. `> nul` 重定向：二次确认替代自动改写

**现状**：`BashSystemActuator.RewriteWindowsNullRedirect` 自动把 `> nul` 改写为 `> /dev/null`（静默修正）。

**问题**：
1. 静默改写掩盖问题 — AI 不知道自己用错了语法，下次还会犯
2. 无法防止外部 agent（如通过 MCP 连接的其他 AI）的碎碎文字中意外包含 `> nul`
3. 改写只在 `BashSystemActuator` 路径，其他执行路径不覆盖

**新方案：二次确认（两轮输入校验）**

不改写命令，而是检测到 `> <保留设备名>` / `2><保留设备名>` / `>><保留设备名>` 等重定向时**返回警告并要求用户再次输入同样的命令**才能执行。保留设备名集合 = {nul, con, prn, aux, com1-9, lpt1-9}。

**设计理由**：
- AI 通过 MCP 发送碎碎文字时，可能意外包含 `> nul`/`> con` 等，二次确认能校验是否为真实意图
- 用户第一轮看到警告 → 确认是真实需求 → 第二轮输入同样命令 → 放行
- 用户第一轮看到警告 → 发现是 AI 误生成 → 不再输入 → 阻止执行
- 不掩盖问题，AI 会收到"你用了 `> nul` 语法，在 git bash 中会创建文件"的反馈
- **拦截 `>` + 保留设备名集合，而非 `>` + 单个文件名** — AI 换成 `> con`/`> prn` 也被拦截，不会绕过

**实现**：新增 `NullRedirectTwoPhaseGuard`（`ICommandGuard` 实现），替代现有 `RewriteWindowsNullRedirect` 的自动改写策略。

**文件**：`core/safety/Guard/src/Hooks/Execution/Interception/Guards/NullRedirectTwoPhaseGuard.cs`（新增）

```
拦截逻辑：
  1. 检测命令含 > <保留设备名> / 2><保留设备名> / >><保留设备名> / <<保留设备名> 等重定向
     保留设备名集合 = {nul, con, prn, aux, com1-9, lpt1-9}（大小写不敏感）
     扩展现有 NullRedirectRegex，把 \bnul\b 改为 \b(nul|con|prn|aux|com[1-9]|lpt[1-9])\b
  2. 第一轮：返回 Redirect(警告消息) — 不执行，要求用户再次输入
     警告消息："检测到 > <设备名> 重定向，在 git bash 中会创建名为 <设备名> 的文件（Windows 保留设备名）。
              若本意是丢弃输出，请改用 > /dev/null。若确需执行，请再次输入同样命令。"
  3. 第二轮：用户再次输入同样命令 → 放行（标记为已二次确认）
  4. 会话级记忆：同一命令二次确认后，后续相同命令不再拦截
```

**与现有 `RewriteWindowsNullRedirect` 的关系**：
- 保留 `RewriteWindowsNullRedirect` 的正则 `NullRedirectRegex`（复用检测逻辑）
- 把改写策略改为二次确认策略（`RewriteWindowsNullRedirect` 标记 `[Obsolete]` 或改为内部调用 `NullRedirectTwoPhaseGuard`）
- 测试从"验证改写结果"改为"验证两轮确认流程"

**粒度分析**：
- `> nul` **重定向** — 拦截粒度合理，git bash 中几乎没有合法的重定向到保留设备名的场景
- `touch nul` **创建** — 不拦截，这是合法的文件创建需求（虽然罕见）
- **拦截核心是重定向符号 `>` + 保留设备名集合**（nul/con/prn/aux/com1-9/lpt1-9），不是单个文件名 — 不检查特定 `>`+`nul` 组合，否则 AI 换个保留名就绕过
- 粒度精确：只拦**重定向操作**（`>`/`>>`/`<` + 保留设备名），不拦**文件操作**（`touch`/`cp`/`mv` + 任意文件名）

## 修改位置汇总

| # | 文件 | 修改类型 | 说明 |
|---|------|----------|------|
| 1 | `core/safety/Guard/src/Security/DangerClassification/DangerCommandDefinitions.cs` | 新增条目 | Execution 级添加 `robocopy` 命令定义 |
| 2 | `core/safety/Guard/src/Security/DangerClassification/DangerousCommandCatalog.Flags.cs` | 新增条目 | Flags 添加 `/MIR`、`/PURGE` → Dangerous；Combinations 添加 4 条组合；DangerousPaths 添加 `\\?\`、`\\.\` 前缀 |
| 3 | `core/safety/Guard/src/Hooks/Execution/Interception/Guards/RobocopyMirrorGuard.cs` | 新增文件 | 保留名清理场景白名单放行守卫 |
| 4 | `core/safety/Guard/src/Hooks/Execution/Interception/Guards/CmdIndirectCallGuard.cs` | 新增文件 | `cmd /c`/`powershell -Command` 间接调用内层命令递归分类守卫 |
| 5 | `core/safety/Guard/src/Hooks/Execution/Interception/Guards/NullRedirectTwoPhaseGuard.cs` | 新增文件 | `> nul` 重定向二次确认守卫（替代 `RewriteWindowsNullRedirect` 自动改写） |
| 6 | `core/execution/Hands/src/SystemActuator/Instances/BashSystemActuator.cs` | 改造 | `RewriteWindowsNullRedirect` 改为委托 `NullRedirectTwoPhaseGuard`，正则 `NullRedirectRegex` 保留复用 |
| 7 | `foundation/Abstractions/04-guard/Security/Scanning/SecurityPatterns.cs` | 修复缺口 | `DosDeviceNameRegex` 扩展匹配裸 `NUL`/`CON`/`PRN`/`AUX` 作为完整文件名（不只是扩展名位置） |
| 8 | `core/safety/Guard/src/Security/PathConstraintValidator.cs` | 修复缺口 | `ValidateOutputRedirections` 中 `NUL` 白名单放行改为需二次确认（与 `NullRedirectTwoPhaseGuard` 联动） |
| 9 | `core/safety/Guard/src/Hooks/Execution/Interception/Guards/` 的 DI 注册 | 自动 | `[Register(typeof(ICommandGuard))]` 标注，源码生成器自动收集 |

> 注：本 ADR 仅设计方案，不修改代码。实现时需按 ADR [0081](0081-seven-layer-build-strategy.md) 七层编译顺序，在 `core/safety/Guard` 层内完成编译验证。

## 替代方案

### 方案 A：robocopy 全命令黑灯（拒绝）

将 `robocopy` 整个命令定为 Dangerous（黑灯），任何参数都直接拒绝。

- ✅ 最简单，零误判风险
- ❌ 过度拦截：`robocopy src dst /E`（纯复制）也被拒绝，丧失合法用途
- ❌ 保留名文件清理场景无法使用 robocopy，需另找方法（如 cmd del，但已被拦截）

**不采用**：过度拦截影响正常文件同步操作。

### 方案 B：仅拦截 /MIR + 目标在工作目录内

只拦截目标路径在工作目录内的 `robocopy /MIR`，工作目录外放行。

- ✅ 保护生产代码区
- ❌ 工作目录外的误删仍会发生（如 `D:\project\w2` 误填成 `D:\project\JoinCode` 的上层）
- ❌ 路径判断复杂，需解析相对路径、符号链接、`..` 逃逸

**不采用**：路径判断不可靠，且 `/MIR` 本身就是高危操作，无论目标在哪都应确认。

### 方案 C（采用）：参数定黑灯 + 保留名场景白名单放行

`/MIR` 和 `/PURGE` 参数直接定 Dangerous（黑灯），但通过 `RobocopyMirrorGuard` 守卫对"保留名文件清理"这一特定场景白名单放行（降级为红灯确认）。

- ✅ 默认拦截所有 `/MIR`/`/PURGE`，零误判风险
- ✅ 保留名清理场景可用（本 ADR 的起因场景）
- ✅ 复用现有守卫链架构（ADR [0039](0039-command-interception-state-machine.md)），无新机制
- ❌ 多一个守卫类，增加维护成本
- ❌ 放行条件判断较复杂（需检查源目录是否空、目标是否含保留名文件）

**采用理由**：安全性与可用性平衡最佳。默认拒绝是安全底线，白名单放行是精确开口子。

### 方案 D：`> nul` 自动改写为 `> /dev/null`（现有实现）

`BashSystemActuator.RewriteWindowsNullRedirect` 自动把 `> nul` 改写为 `> /dev/null`，静默修正。

- ✅ AI 无感，命令自动修正
- ✅ 已实现且有 12 个测试覆盖
- ❌ 静默改写掩盖问题，AI 不知道用错了语法，下次还会犯
- ❌ 无法防止外部 agent（MCP 连接的其他 AI）的碎碎文字中意外包含 `> nul`
- ❌ 只在 `BashSystemActuator` 路径覆盖，其他执行路径不覆盖

**不采用（作为主策略）**：掩盖问题 + 外部 agent 不覆盖。保留正则复用，改写策略改为方案 E 的二次确认。

### 方案 E（采用）：`> nul` 二次确认（两轮输入校验）

检测到 `> nul` 重定向时不改写，返回警告并要求用户再次输入同样命令才能执行。

- ✅ 不掩盖问题，AI 收到明确反馈"你用了 `> nul` 语法，在 git bash 中会创建文件"
- ✅ 防止外部 agent 碎碎文字误执行（需二次确认）
- ✅ 允许真实需求通过（用户二次输入即可）
- ✅ 粒度精确：拦截重定向操作（`>` + 保留设备名集合），不拦截文件创建（`touch` + 任意文件名）
- ❌ 增加用户交互成本（每次 `> nul` 需二次确认）
- ❌ 需改造现有 `RewriteWindowsNullRedirect` + 测试

**采用理由**：安全 > 便利。二次确认是防止 AI 误执行的最优解，且粒度精确（只拦重定向不拦创建）。

## 后果

### 正面

1. **堵住安全缺口**：`robocopy /MIR` 不再能绕过拦截清空任意目录
2. **堵住同类缺口**：`cmd /c` 间接调用、`\\?\` 长路径前绕过、`Remove-Item -Force` + 长路径组合均被覆盖
3. **堵住根因**：`> <保留设备名>` 重定向二次确认，从源头防止 `nul`/`con`/`prn` 等保留名文件被意外创建
4. **保留名文件清理仍可用**：通过白名单守卫，`nul`/`con` 等保留名文件清理场景不受影响
5. **审计可追溯**：`Combinations` 表中记录了危险组合的描述，拦截日志可输出中文说明
6. **复用现有架构**：不引入新机制，完全在 ADR [0047](0047-unified-danger-level-classification.md) 5 级分级 + ADR [0039](0039-command-interception-state-machine.md) 守卫链框架内

### 负面

1. **新增守卫类**：`RobocopyMirrorGuard` + `CmdIndirectCallGuard` + `NullRedirectTwoPhaseGuard` 增加维护成本
2. **放行条件误判风险**：源目录"空"判断、目标"含保留名文件"判断可能有边界情况
3. **间接调用解析复杂度**：`cmd /c` 内层命令解析需处理引号、转义、`&&`/`||`/`|` 管道等复杂语法
4. **用户体验**：合法的 `robocopy /MIR` 同步场景和 `> nul` 重定向需二次确认，增加交互成本
5. **`\\?\` 前缀误拦**：部分合法的长路径操作可能被误拦为红灯
6. **现有测试需改造**：`BashSystemActuatorNullRedirectTests` 从验证改写结果改为验证两轮确认流程

### 验证清单

实现后需验证：

**robocopy 拦截**：
- [ ] `robocopy src dst /MIR` 在 Ask 模式下被黑灯拒绝（非保留名场景）
- [ ] `robocopy src dst /MIR` 在 Bypass 模式下仍被拒绝（黑灯安全红线）
- [ ] `robocopy 空目录 含nul目录 /MIR` 在 Ask 模式下红灯待确认（保留名场景放行）
- [ ] `robocopy src dst /E`（纯复制）在 Ask 模式下红灯待确认（robocopy 基础命令，非黑灯）
- [ ] `robocopy src dst /PURGE` 被黑灯拒绝（/PURGE 同 /MIR 处理）

**同类缺口拦截**：
- [ ] `cmd /c "del file"` 内层 `del` 被递归分类为红灯（Execution）
- [ ] `cmd /c "del \\?\path\nul"` 内层 `del` + 长路径被分类为黑灯（Dangerous）
- [ ] `powershell -Command "Remove-Item -Force '\\?\path'"` 被分类为黑灯
- [ ] 含 `\\?\` 前缀的路径至少分类为红灯
- [ ] 含 `\\.\` 前缀的路径至少分类为红灯
- [ ] `cmd /c "echo hello"` 内层 `echo` 被分类为白灯（Safe），不误拦合法间接调用

**`> <保留设备名>` 重定向二次确认**：
- [ ] `echo x > nul` 第一轮返回警告，不执行
- [ ] `echo x > nul` 第二轮（同样命令）放行执行
- [ ] `echo x > nul` 会话级记忆：二次确认后后续相同命令不再拦截
- [ ] `echo x 2>nul` / `echo x >>nul` / `cat <nul` 等变体均触发二次确认
- [ ] `echo x > con` / `echo x > prn` / `echo x > aux` / `echo x > com1` / `echo x > lpt1` 均触发二次确认（全保留设备名集合覆盖）
- [ ] `touch nul` / `touch con` 不触发二次确认（创建操作，非重定向）
- [ ] `echo x > /dev/null` 不触发二次确认（正确语法）
- [ ] `echo x > NUL` / `echo x > CON` 大小写不敏感触发二次确认
- [ ] `echo x > normal.txt` 不触发二次确认（非保留设备名）

**缺口修复**：
- [ ] `DosDeviceNameRegex` 匹配裸 `NUL`/`CON`/`PRN`/`AUX` 作为完整文件名
- [ ] `PathConstraintValidator` 中 `NUL` 白名单改为需二次确认

**单元测试**：
- [ ] `RobocopyMirrorGuard` 放行条件 5 个分支全覆盖
- [ ] `CmdIndirectCallGuard` 递归分类 + 引号/转义解析
- [ ] `NullRedirectTwoPhaseGuard` 两轮确认流程 + 会话级记忆 + 变体覆盖
- [ ] `DangerousCommandCatalog` 新增条目（robocopy + /MIR + /PURGE + \\?\ + \\.\ + 4 条组合）
- [ ] `SecurityPatterns.DosDeviceNameRegex` 裸设备名匹配

## 关联

- ADR [0047](0047-unified-danger-level-classification.md) — 统一危险指令分级系统（5 级分级）
- ADR [0039](0039-command-interception-state-machine.md) — 命令拦截全状态机 + 守卫链
- ADR [0034](0034-command-interception-layered.md) — 命令拦截分层 Guard+Interceptor+Dispatcher（已 superseded）
- ADR [0081](0081-seven-layer-build-strategy.md) — 七层编译顺序
- ADR [0084](0084-platform-windows-env-rules.md) — 平台专属操作禁令（PowerShell/路径格式）

## 全局功能改造范围（用户 2026-09-13 补充）

本 ADR 除具体的命令拦截规则外，用户要求补充两个**全局功能**的改造范围，两者均在 GUI 上由用户决定打勾开启。

### 功能 1：全局无人值守模式

**需求**：GUI 上提供勾选框"无人值守模式"，开启后 AI 可持续推进长任务，不需要每步确认。适用于用户睡觉/离开等场景，醒来后看到所有任务已完成。

**与现有 `PermissionMode` 的关系**：

| 现有模式 | 行为 | 无人值守关联 |
|---------|------|-------------|
| Plan | 只读规划，拒绝写操作 | — |
| Auto | 自动执行，危险命令拒绝 | 接近但不够 |
| Ask | 每步确认 | 无人值守的反面 |
| Bypass | 全部放行（黑灯仍拒绝） | 最接近但缺安全保护 |

**无人值守模式设计**：
- 介于 Auto 和 Bypass 之间 — 自动执行非黑灯命令，黑灯（Dangerous）仍拒绝
- 红灯（Execution）命令自动执行但记录审计日志，不弹确认
- 绿灯（LightValidation）和黄灯（Unknown）自动执行
- 白灯（Safe）自动执行
- **黑灯（Dangerous）仍直接拒绝** — 无人值守不是放弃安全底线
- 任务调度层：AI 持续推进，不暂停询问"是否继续"

**改造范围**：

| # | 层 | 文件/模块 | 改造内容 |
|---|---|----------|---------|
| 1 | GUI | ViewModel/Settings | 添加"无人值守模式"勾选框，绑定 `IsUnattendedMode` 属性 |
| 2 | 权限 | `DangerousCommandProtectionMiddleware` | 新增 `UnattendedMode` 分支：红灯自动执行+审计日志，不弹确认 |
| 3 | 权限 | `PermissionMode` 枚举或新增 `Unattended` 模式 | 或复用 Bypass + 额外的红灯审计逻辑 |
| 4 | 任务调度 | Agent 主循环 | 无人值守时不暂停询问，持续推进直到完成或黑灯拒绝 |
| 5 | 配置 | `settings.json` | 持久化 `isUnattendedMode` 字段，支持热重载 |

### 功能 2：防丢字符二次确认

**前因**：推理时开启 MTP（Multi-Token Prediction，多令牌预测）加速后，可能丢字符/乱入字符。MTP 是一种利用"多令牌预测"能力让大模型一次性生成多个词的推理加速技术：

- **传统自回归生成**：逐字串行生成，每生成一个词都要把整个模型权重从显存读一遍，瓶颈在内存带宽而非算力
- **MTP 机制**：利用模型内置的 MTP 预测头，在单次前向传播中同时预测多个候选词 → 主模型一次性验证这批草稿 → 猜对的直接采纳，猜错的从错误位置重来 → 减少前向传播次数，提升速度
- **丢字符风险**：MTP 的"先草稿再验收"机制在草稿预测/验证环节可能引入字符级错误 — 草稿词预测偏差、验证采纳边界条件、并行解码竞态等，导致最终输出字符串丢字符或乱入字符

AI 生成的命令字符串经 MTP 加速生成后可能被截断或混入乱码，导致执行的是错误命令甚至危险命令（如 `rm -rf /` 丢了个字符变成 `rm -rf`，或重定向目标乱码指向意外路径）。

**后果**：丢字符导致的命令变形不可预测，常规命令拦截（按命令名/参数匹配）无法防御，因为变形后的命令可能完全不匹配任何已知模式。

**需求**：GUI 上提供勾选框"防丢字符二次确认"，开启后**任何命令执行都需要 AI 二次确认**（两轮输入校验）。收到命令后不立即执行，要求再次输入同样命令 → 两轮输入字符串完全一致才放行 → 确保命令经 MTP 加速生成后完整无误。这是一个全局拦截，不是针对特定命令（如 `> nul`），而是所有命令。**本质是通信可靠性保障，不是权限控制。**

**与本 ADR 第 7 节 `NullRedirectTwoPhaseGuard` 的关系**：
- `NullRedirectTwoPhaseGuard` 是**针对特定危险命令**的二次确认（`>` + 保留设备名）
- 全局二次确认是**所有命令**都需二次确认，粒度更大
- 两者可共存：全局二次确认开启时，`NullRedirectTwoPhaseGuard` 的特定拦截仍优先匹配（给出更具体的警告消息）

**设计**：
- 全局拦截中间件，在命令执行管道最前端插入
- 第一轮：收到命令 → 返回"请再次输入同样命令确认执行"
- 第二轮：用户再次输入同样命令 → 放行
- 会话级记忆：同一命令二次确认后，后续相同命令不再拦截（避免重复确认）
- **白灯（Safe）命令可选豁免** — 用户可勾选"只读命令免确认"，避免 `ls`/`cat` 等也要二次确认

**改造范围**：

| # | 层 | 文件/模块 | 改造内容 |
|---|---|----------|---------|
| 1 | GUI | ViewModel/Settings | 添加"防丢字符二次确认"勾选框，绑定 `IsAntiCharLossConfirm` 属性 |
| 2 | 拦截 | `GlobalTwoPhaseConfirmGuard`（新增） | `ICommandGuard` 实现，所有命令第一轮返回 Redirect，第二轮放行 |
| 3 | 拦截 | `ShellCommandInterceptionMiddleware` | 守卫链最前端插入 `GlobalTwoPhaseConfirmGuard`（最高优先级） |
| 4 | 会话 | 会话状态管理 | 二次确认的命令记忆（会话级，不持久化） |
| 5 | 配置 | `settings.json` | 持久化 `isAntiCharLossConfirm` 字段 + `safeCommandExempt` 子选项，支持热重载 |
| 6 | GUI | ViewModel/Settings | 可选子勾选框"只读命令免确认"（白灯豁免） |

### 两个功能的关系

两个功能**不矛盾**，可同时开启，由用户自行勾选组合。防丢字符二次确认的本质是**防止 MTP 加速推理时丢字符/乱入字符导致命令变形**（通信可靠性保障），不是权限控制，与无人值守模式无关。

- **专项守卫**：`NullRedirectTwoPhaseGuard` — 针对 `>` + 保留设备名等特定危险命令的二次确认
- **全局守卫**：`GlobalTwoPhaseConfirmGuard` — 所有命令的防丢字符二次确认（防 MTP 加速丢字符）
- 两者并存：全局守卫先匹配所有命令做字符串一致性校验，专项守卫对特定命令给出更具体的警告消息

| 无人值守 | 防丢字符二次确认 | 行为 |
|---------|----------------|------|
| ❌ | ❌ | 默认行为（Ask 模式逐步确认） |
| ✅ | ❌ | AI 持续推进，红灯自动执行+审计，黑灯拒绝 |
| ❌ | ✅ | 每条命令都需二次确认（防 MTP 加速丢字符） |
| ✅ | ✅ | AI 持续推进 + 每条命令防丢字符二次确认（无人值守但防丢字符） |

**不互斥**：两个勾选框独立，用户自行组合。GUI 不做互斥限制。
