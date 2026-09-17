# MTP 扰动下 Agent Bash 纵深防御实施计划

> 📍 **导航**: [docs/](../../README.md) › [plan/](../README.md) › [safety/](README.md)
> 🔗 **相关 ADR**: [0034](../../adr/0034-command-interception-layered.md) 命令拦截分层 · [0036](../../adr/0036-defense-in-depth-l1-l10.md) 纵深防御 L1-L10 · [0039](../../adr/0039-command-interception-state-machine.md) 命令拦截全状态机 · [0047](../../adr/0047-unified-danger-level-classification.md) 统一危险指令分级
> 📌 **建议新 ADR**: 0113 — MTP 扰动专项防御（待确认是新建还是补充 0036）

## 一、背景与目标

MTP（Multi-Token Prediction）推理加速会产生偶发的单字符级扰动。表面是"AI 打错字"，本质是**可撤回操作和不可撤回操作的安全边界被击穿**：write/edit 错了可重写，但 bash 一旦执行副作用就固化。

典型案例：`>/dev/null` 被 MTP 损坏成 `>nul`，在 Git Bash 下 `nul` 是普通文件名，产生难以删除的文件。

**目标**：在扰动必然存在的前提下，保证 bash 工具的副作用可控。不关 MTP（供应商侧，关掉损失吞吐），在 Agent 层兜住。

## 二、现状分析（基于代码探索）

### 2.1 已有基础设施（直接复用）

| 组件 | 位置 | 状态 |
|------|------|------|
| 守卫+拦截器双层架构 | `lib/guard/hooks/execution/interception/core/` (`ICommandGuard`/`ICommandInterceptor`/`CommandInterceptionDispatcher`) | ✅ 完整，三阶段调度 |
| 五色灯分级 | `CommandDangerLevel` 枚举 (Safe/Unknown/LightValidation/Execution/Dangerous) + `ICommandDangerClassifier` | ✅ 完整 |
| AhoCorasick 前缀树 | `lib/infrastructure/utils/text/AhoCorasick.cs` + `DualBufferAhoCorasick`（热重载） | ✅ 完整 |
| 结构化解析 | `ShellCommand.Parse`（引号处理）+ `BashAstParser`（TreeSitter AST）+ `BashAstSecurityWalker`（argv 构建） | ✅ 存在，**需验证严格模式** |
| 危险命令目录 | `DangerousCommandCatalog`（源码生成器扫描 `[DangerCommand]`）+ `DangerCommandDefinitions` + `Flags.cs` | ✅ 完整，唯一数据源 |
| Windows 保留设备名 | `RetainedDeviceNames`（22 个设备名 + 正则） | ✅ 完整，唯一数据源 |
| 重定向守卫（黑名单） | `NullRedirectTwoPhaseGuard`（P=700，拦截 >nul 等） | ✅ 存在，**拒绝消息未封死替代路径** |
| git 守卫 | `GitCommitGuard`（P=1000）+ `CommandDangerClassifier.ClassifyGitEarlyReturn`（只读/不可撤回分级） | ✅ 完整 |
| 二次确认 | `GlobalTwoPhaseConfirmGuard`（P=900）+ `IConfirmationGate` + `GuardConfirmMode.AntiCharLossConfirm` | ✅ 存在，**无 hash 防意图反推** |
| 审计 | `ICommandExecutionAuditor` + `CommandExecutionAuditor`（JSONL）+ `IFileSystemSnapshotService` | ✅ 完整 |
| 路径规范化 | `PathValidator.NormalizePath` + `PathConstraintValidator.ExpandTilde/ResolvePath` + ADR 0095 `PathNormalizer` | ✅ 完整 |
| 工作区边界 | `PathValidator.IsPathWithinWorkspace` + `PathPermissionChecker.IsInWorkingDirectory` | ✅ 完整 |
| 路径逃逸检测 | `PathValidator.PathEscapePatterns` + `SoftSandboxProvider.ResolveSymlinkTarget` | ✅ 完整 |
| 拒绝消息模板 | `ICommandRiskHandler.BuildRejectionMessage/BuildConfirmationMessage` + 9 个 RiskHandler | ✅ 完整 |
| 配置开关 | `SettingsJson.IsAntiCharLossConfirm`（行 220）+ `SettingsLoader` + `IConfigChangeNotifier` | ✅ 开关已存在 |
| Hook 事件 | `HookEvent.PostToolUse` + `PostToolUseHookMiddleware` | ✅ 完整 |
| 循环检测器（可参考） | `OutputLoopDetector` + `ShannonEntropyDetector`（状态机）+ `InformationEntropyGuardian` | ✅ 可复用状态机模式 |
| `GuardContext` | 已有 `ConfirmMode`/`ConfirmedCommand` | ✅ **缺 ArgvHash 字段** |
| `CommandDecision` | Allow/Rewrite/Deny/Redirect/Handoff | ✅ **Deny 未带"封死替代路径"结构化字段** |

### 2.2 缺失项（需要新建/增强）

| 编号 | 缺失项 | 严重度 | 说明 |
|------|--------|--------|------|
| D1 | **二次确认 hash 防意图反推** | 🔴 高 | `GlobalTwoPhaseConfirmGuard` 用 `ConfirmedCommand == command` 字符串相等，AI 可照抄通过，不走解析。需加 argv hash |
| D2 | **拒绝消息封死替代路径** | 🔴 高 | `NullRedirectTwoPhaseGuard` 未显式列出 `>NUL / >nul. / >con` 等同类禁止写法，未提示 MTP 扰动重生成 |
| D3 | **重定向白名单** | 🔴 高 | 当前只有黑名单（拦截设备名），缺白名单（只允许工作区路径 + `/dev/null`）。需在路径规范化后判定 |
| D4 | **MTP 扰动检测器** | 🟡 中 | PostToolUse 扰动统计（字符偏差/路径拼写偏移/重定向符号异常），完全不存在 |
| D5 | **自适应开关** | 🟡 中 | `IsAntiCharLossConfirm` 开关存在但无基于统计特征的自适应触发（当前靠手动开启） |
| D6 | **严格解析模式验证** | 🟡 中 | `ShellCommand.Parse` 引号不配对是否直接拒绝？需验证，可能需增强 |
| D7 | **分位置 AC 过滤验证** | 🟢 低 | `CommandDangerClassifier` 是否分 argv[0]/argv[1..n] 位置匹配？需验证 |
| D8 | **git 全局参数黑灯** | 🟢 低 | `git -c`/`--exec-path`/`--config-env` 可改变 git 执行语义，应直接黑灯 |

## 三、设计点（8+1 条不可妥协约束）

从用户规范提取，作为实施期和未来逆改期的单一参照源：

1. **禁止自动改写 bash 命令** — 校验失败就报错，让 AI 重新生成。不在错误字符串上局部修补
2. **结构化解析是承重墙** — 解析失败（引号/括号/heredoc 不配对）直接拒绝，不进入下游
3. **重定向走白名单** — 不枚举危险名，只允许工作区路径 + `/dev/null`。规范化后判定
4. **AC 分位置作用** — argv[0] 匹配黑灯命令名，argv[1..n] 匹配危险参数，不全局扫原始串
5. **拒绝时封死替代路径** — 显式列出同类禁止写法 + 提示 MTP 扰动重生成
6. **PostToolUse 只审计不清理** — 清理本身也是 bash 操作，补救不能代替预防
7. **自适应开关基于统计特征** — 不依赖 typo 关键字，靠字符偏差/路径偏移统计
8. **路径白名单在规范化之后判定** — 变量/波浪号展开 + normpath，否则白名单形同虚设
9. **二次确认串含 argv hash** — 防 AI 从意图反推确认串，hash 必须由解析结果计算得出

## 四、Node 化架构设计（核心要求）

> 用户要求：① 把执行链 node 化，随时可组装各种拦截设备 ② LINQ 链式+有名函数风格 ③ 设计也 node 化
> 对齐现有 `WriteDefense` 模式（ADR 0104：一切皆为 node/插件）

### 4.1 现有 node 模式参考（WriteDefense）

```
WriteDefenseContext（可变状态，各步骤间传递）
WriteDefenseStepAsync 委托（统一签名：ValueTask<ToolResult?> (ctx, ct)，null=通过，非null=拒绝短路）
WriteDefense 构建器（Begin().Then(step1).Then(step2).ExecuteAsync()，顺序执行任一短路）
WriteDefenseService（薄编排层，注入各 node，提供有名函数作为步骤）
各 XxxGuardNode（独立公共对象 [Register(typeof(XxxNode), Singleton)]，纯函数/异步方法）
```

### 4.2 BashDefense 链式构建器（新增）

```
BashDefenseContext（可变状态）
  ├── OriginalCommand      原始命令
  ├── CurrentCommand       当前命令（可能被改写）
  ├── WorkingDirectory     工作目录
  ├── ShellKind            Shell 类型
  ├── ConfirmMode          确认模式
  ├── ConfirmedCommand     已确认命令
  ├── ArgvHash             argv hash（防意图反推）
  ├── ParsedCommand        解析结果（StrictParse 步骤填充）
  ├── DangerLevel          危险等级（Classify 步骤填充）
  └── ExecutionResult      执行结果（PostToolUse 填充）

BashDefenseStepAsync 委托
  ValueTask<ToolResult?> (BashDefenseContext ctx, CancellationToken ct)
  null = 通过，非 null = 拒绝短路

BashDefense 构建器
  Begin(command, workingDirectory, shellKind, confirmMode, confirmedCommand, argvHash)
  .Then(step1).Then(step2)...ExecuteAsync(ct)
  → (BashDefenseContext, ToolResult? rejection)
```

### 4.3 拦截设备 Node 清单（每个独立公共对象）

| Node | 职责 | 对应缺失项 | 阶段 |
|------|------|-----------|------|
| `StrictParseNode` | 严格解析（引号/括号/heredoc 不配对→拒绝） | D6 | 4 |
| `RetainedDeviceNode` | 保留设备名检测（现有 NullRedirectTwoPhaseGuard 逻辑提取） | 增强 | 1 |
| `RedirectWhitelistNode` | 重定向白名单（规范化后判定工作区内或 /dev/null） | D3 | 2 |
| `ArgvHashNode` | argv hash 校验（防意图反推） | D1 | 1 |
| `GitGlobalParamNode` | git 全局参数黑灯（-c/--exec-path/--config-env） | D8 | 1 |
| `DangerClassifyNode` | 五色灯分级（委托现有 CommandDangerClassifier） | 复用 | 1 |
| `MtpPerturbationNode` | 扰动统计 + 自适应触发 | D4/D5 | 3 |
| `RejectMessageNode` | 拒绝消息模板（封死替代路径 + 提示重生成） | D2 | 1 |

### 4.4 BashDefenseService（薄编排层）

注入各 node，提供**有名函数**（不是 lambda）作为步骤：

```csharp
// 调用方组装（LINQ 链式 + 有名函数）
var (ctx, rejection) = await _bashDefenseService
    .Begin(command, workDir, shellKind, confirmMode, confirmedCmd, argvHash)
    .Then(_bashDefenseService.StrictParse)          // 严格解析
    .Then(_bashDefenseService.CheckRetainedDevice)   // 保留设备名
    .Then(_bashDefenseService.CheckRedirectWhitelist)// 重定向白名单
    .Then(_bashDefenseService.CheckGitGlobalParam)   // git 全局参数
    .Then(_bashDefenseService.RequireArgvHash)       // hash 防意图反推
    .Then(_bashDefenseService.ClassifyDangerLevel)   // 五色灯分级
    .ExecuteAsync(ct);

if (rejection is not null) return rejection;
// 执行 ctx.CurrentCommand
```

### 4.5 与现有 CommandInterceptionDispatcher 的关系

**共存**，不替换：
- `CommandInterceptionDispatcher`（DI 自动收集）— 保留，全局固定守卫（GitCommit、Heredoc、Vpn 等）
- `BashDefense`（链式构建器）— 新增，可组装的 MTP 扰动防御链

`ShellCommandInterceptionMiddleware` 调用 `CommandInterceptionDispatcher` 之后，再调用 `BashDefense` 链。或者 `BashDefense` 作为独立中间件插入管道。

## 五、实施阶段（渐进式，每步编译+测试+commit）

> ✅ **全部阶段完成** — BashDefense 链已完整接入执行管道。当前组装：`StrictParse + CheckRetainedDevice + CheckRedirectWhitelist + RequireArgvHash`。`MtpPerturbationNode` 已接入 PostToolUse 审计中间件。ADR [0113](../../adr/0113-mtp-perturbation-bash-defense.md) 已 accepted。

### 阶段 1：增强现有守卫 ✅ 完成

| 步骤 | 状态 | commit |
|------|------|--------|
| 1.1 `BashDefenseContext` + `ArgvHash` 字段 | ✅ | a4f22c18b |
| 1.2 `ArgvHashNode` + `RequireArgvHash`（hash 防意图反推） | ✅ | a4f22c18b |
| 1.3 `RetainedDeviceNode` + 封死替代路径 | ✅ | 5a8e92dcf 前 |
| 1.4 git 全局参数黑灯 | ✅ | 5a8e92dcf |

### 阶段 2：重定向白名单 ✅ 完成

| 步骤 | 状态 | commit |
|------|------|--------|
| 2.1 `RedirectWhitelistNode` | ✅ | 最新 |
| 2.2 白名单判定（规范化后） | ✅ | 最新 |
| 2.3 拒绝消息（回显+封死替代路径） | ✅ | 最新 |

### 阶段 3：MTP 扰动检测器 ✅ 完成（未接入 PostToolUse）

| 步骤 | 状态 | commit |
|------|------|--------|
| 3.1 `MtpPerturbationNode` | ✅ | 最新 |
| 3.2 扰动统计特征 | ✅ | 最新 |
| 3.3 自适应触发 | ✅ | 最新 |
| 3.4 接入 PostToolUse | ✅ | ShellPerturbationAuditMiddleware |

### 接入执行管道 ✅ 完成

| 步骤 | 状态 | commit |
|------|------|--------|
| `ShellCommandInterceptionMiddleware` 接入 | ✅ | 最新 |
| `GlobalTwoPhaseConfirmGuard` 归档 | ✅ | 最新 |
| `NullRedirectTwoPhaseGuard` 归档 | ✅ | 5a8e92dcf 前 |
| `RequireArgvHash` 接入（需配置读取） | ✅ | IOptions<WorkflowConfig> |

### 阶段 1：增强现有守卫（低风险，改现有文件）

> 目标：补齐 D1、D2、D8，不新增组件，只增强现有守卫

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| 1.1 | `GuardContext` 增加 `ArgvHash` 字段（`string?`，可选） | `GuardContext.cs` |
| 1.2 | `GlobalTwoPhaseConfirmGuard` 增强：确认时校验 `ArgvHash`，拒绝消息回显结构化解析结果 + hash | `GlobalTwoPhaseConfirmGuard.cs` |
| 1.3 | `NullRedirectTwoPhaseGuard` 拒绝消息增强：封死替代路径（`>NUL / >nul. / >con`）+ 提示 MTP 扰动重生成 | `NullRedirectTwoPhaseGuard.cs` |
| 1.4 | `DangerousCommandCatalog.Flags.cs` 增加 git 全局参数组合（`git -c`/`--exec-path`/`--config-env`）→ Dangerous | `DangerousCommandCatalog.Flags.cs` |

### 阶段 2：重定向白名单（中风险，新增守卫）

> 目标：补齐 D3，新增重定向白名单守卫

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| 2.1 | 新建 `RedirectWhitelistGuard`（P=750，在 `NullRedirectTwoPhaseGuard` 之后）：解析重定向目标 → 变量/波浪号展开 → normpath → 判定是否在工作区内或等于 `/dev/null` | `lib/guard/hooks/execution/interception/guards/RedirectWhitelistGuard.cs` |
| 2.2 | 白名单判定委托 `PathValidator.IsPathWithinWorkspace` + `PathConstraintValidator.ExpandTilde/ResolvePath` | 复用现有 |
| 2.3 | 拒绝消息：回显解析后的重定向目标 + 规范化结果 + 拒绝原因 + 正确写法 | 同 2.1 |

### 阶段 3：MTP 扰动检测器（中风险，新增组件）

> 目标：补齐 D4、D5，PostToolUse 扰动统计 + 自适应开关

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| 3.1 | 新建 `MtpPerturbationTracker`：记录每次工具调用的原始输入/解析结果/执行结果/与历史成功模式的偏差 | `lib/guard/security/auditing/MtpPerturbationTracker.cs` |
| 3.2 | 扰动统计特征：字符长度偏差 ±1~2、已知路径拼写偏差、重定向符号异常 | 同 3.1 |
| 3.3 | 自适应触发：连续 N 次偏差 → 自动启用 `AntiCharLossConfirm` 模式（或降低 MTP 权重） | 同 3.1 |
| 3.4 | 接入 `PostToolUseHookMiddleware`：执行后调用 `MtpPerturbationTracker.Record` | `PostToolUseHookMiddleware` 或新增中间件 |

### 阶段 4：严格解析模式验证与增强 ✅ 完成

> 目标：补齐 D6、D7，验证现有解析器是否满足严格模式

| 步骤 | 内容 | 涉及文件 | 状态 |
|------|------|----------|------|
| 4.1 | 验证 `ShellCommand.Parse` 引号不配对行为：写测试覆盖边界 case 清单 | `ShellCommand.cs` + 测试 | ✅ 确认静默接受 |
| 4.2 | 新增 `StrictParseNode` 检测未闭合引号，接入防御链首位 | `StrictParseNode.cs` | ✅ |
| 4.3 | 修复 `CommandDangerClassifier` AC 分位置匹配：`echo "rm -rf /"` 不再误杀 | `CommandDangerClassifier.cs` + 12 测试 | ✅ |

## 五、边界 case 清单（测试必须覆盖）

| # | 输入 | 预期 |
|---|------|------|
| 1 | `cmd >nul` | 拒绝（重定向到保留设备名） |
| 2 | `echo "hello >nul"` | 放行（引号内非重定向） |
| 3 | `echo "hello >nul` | 拒绝（引号未闭合，严格解析） |
| 4 | `git -c core.sshCommand="rm -rf ~" fetch` | 黑灯（git 全局参数注入） |
| 5 | `git push --force-with-lease` | 红灯 + 确认（远端不可逆） |
| 6 | `rm ./tmp >/dev/null` | 红灯 + 确认 |
| 7 | `cmd > ../../etc/passwd` | 拒绝（规范化后逃出工作区） |
| 8 | `cmd > $HOME/.bashrc` | 拒绝（变量展开后逃出工作区） |
| 9 | `cmd > ./build/output.txt` | 放行（工作区内） |
| 10 | `cmd >/dev/null 2>&1` | 放行（标准丢弃） |
| 11 | 二次确认：AI 照抄命令但 hash 不对 | 拒绝（防意图反推） |
| 12 | 二次确认：AI 重新解析生成正确 hash | 放行 |

## 六、ADR 建议

**建议新建 ADR 0113 — MTP 扰动专项防御**

理由：这是新的架构决策（选择"在 Agent 层兜住 MTP 扰动"放弃"关掉 MTP"），涉及跨模块（守卫/审计/配置/解析器），影响全局 bash 工具安全边界。符合 ADR 触发条件。

ADR 内容要点：
- 决策：在 Agent 层建立 MTP 扰动纵深防御，不依赖供应商关 MTP
- 替代方案：① 关掉 MTP（放弃吞吐，不可行）② 仅 PostToolUse 补救（已否决，补救≠预防）③ 仅手动开关（已否决，扰动是间歇性的）
- 8+1 条不可妥协约束
- 实施阶段引用本计划

## 七、验证清单

- [x] 阶段 1：`ArgvHashNode` + `RetainedDeviceNode` 封死替代路径 + git 全局参数黑灯
- [x] 阶段 2：`RedirectWhitelistNode` 白名单判定（规范化后）
- [x] 阶段 3：`MtpPerturbationNode` 扰动统计 + 自适应触发 + PostToolUse 审计中间件
- [x] 阶段 4：严格解析检测（StrictParseNode）+ 分位置 AC 匹配修复
- [x] 边界 case 1-12 测试覆盖（引号内危险子串不误杀 + 未闭合引号拒绝）
- [x] 编译通过（Debug 模式）
- [x] 单元测试通过（58 BashDefense + 12 AC分位置 + 10 StrictParse + 5 审计中间件）
- [x] ADR 0113 状态 accepted

## 八、决策依据

- 用户规范：《MTP 扰动下的 Bash 工具纵深防御设计》+ 评审定稿 + 意图反推分析
- 现有架构：ADR 0034/0036/0039/0047 已落地的守卫+拦截器+五色灯体系
- 复用原则：不重复造轮子，复用 `DangerousCommandCatalog`/`RetainedDeviceNames`/`PathValidator`/`AhoCorasick` 等现有组件
