# 0113. MTP 扰动下 Agent Bash 纵深防御

- 状态：accepted
- 日期：2026-09-17
- 决策者：用户 + AI

## 背景

MTP（Multi-Token Prediction）推理加速会产生偶发的单字符级扰动。表面是"AI 打错字"，本质是**可撤回操作和不可撤回操作的安全边界被击穿**：write/edit 错了可重写，但 bash 一旦执行副作用就固化。

典型案例：`>/dev/null` 被 MTP 损坏成 `>nul`，在 Git Bash 下 `nul` 是普通文件名，产生难以删除的文件。

现有基础设施已有守卫+拦截器+五色灯+审计体系，但存在以下缺失：
- 二次确认用字符串相等比较，无 hash 防意图反推
- 重定向只有黑名单（拦截设备名），缺白名单（只允许工作区路径 + `/dev/null`）
- AC 危险组合匹配扫描整个原始命令字符串，引号内危险子串会误杀（`echo "rm -rf /"` 被误判为 Execution 级）
- `ShellCommand.Parse` 静默接受未闭合引号，MTP 丢字符导致的引号不配对会进入下游执行
- 无 PostToolUse 扰动统计和自适应触发机制

## 决策

在 Agent 层建立 MTP 扰动纵深防御链（BashDefense），不依赖供应商关 MTP。

### 架构：node 化链式构建器

对齐现有 `WriteDefense` 模式（ADR 0104），采用 `Begin().Then().Then().ExecuteAsync()` 链式构建器：

```
ShellCommandInterceptionMiddleware
  → CommandInterceptionDispatcher（现有守卫链）
  → BashDefense 链（新增 MTP 扰动防御）
      → StrictParse（未闭合引号检测）
      → CheckRetainedDevice（保留设备名检测）
      → CheckRedirectWhitelist（重定向白名单）
      → RequireArgvHash（argv hash 二次确认）
  → ShellExecutionMiddleware
  → ShellOutputMiddleware
  → ShellPerturbationAuditMiddleware（新增 PostToolUse 扰动统计）
```

### 8+1 条不可妥协约束

1. 禁止自动改写 bash 命令 — 校验失败就报错，让 AI 重新生成
2. 结构化解析是承重墙 — 引号不配对直接拒绝，不进入下游
3. 重定向走白名单 — 只允许工作区路径 + `/dev/null`，规范化后判定
4. AC 分位置作用 — argv[0] 匹配命令名，argv[1..n] 匹配参数，不全局扫原始串
5. 拒绝时封死替代路径 — 显式列出同类禁止写法 + 提示 MTP 扰动重生成
6. PostToolUse 只审计不清理 — 补救不能代替预防
7. 自适应开关基于统计特征 — 不依赖 typo 关键字
8. 路径白名单在规范化之后判定 — 变量/波浪号展开 + normpath
9. 二次确认串含 argv hash — 防 AI 从意图反推确认串

### 实施阶段

| 阶段 | 内容 | 状态 |
|------|------|------|
| 1 | 增强现有守卫（ArgvHash + RetainedDevice + git 全局参数黑灯） | ✅ |
| 2 | 重定向白名单（RedirectWhitelistNode） | ✅ |
| 3 | MTP 扰动检测器（MtpPerturbationNode + PostToolUse 审计中间件） | ✅ |
| 4 | 严格解析检测（StrictParseNode）+ 分位置 AC 匹配修复 | ✅ |

### 手动验证记录（ADR 0080 格式，2026-09-17）

> exe 路径：`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`（全量 `--no-incremental` 构建，时间戳 22:21）
> 调用方式：`jcc.exe mcp_call bash '{"command":"...","working_directory":"D:\\project\\w2"}'`
> 工作目录：`D:\project\w2`

| # | 场景 | 命令 | 预期 | 实际 | 结果 |
|---|------|------|------|------|------|
| 1 | 保留设备名 `>nul` | `echo test >nul` | JCC9005 拦截 | JCC9005 拦截 + 封死替代路径 | ✅ |
| 2 | 未闭合双引号 | `echo "hello world` | JCC9010 拦截 | JCC9010 拦截 + MTP 提示 | ✅ |
| 3 | 重定向到 `/etc/passwd` | `echo test > /etc/passwd` | JCC9009 拦截 | JCC9009 拦截 + 规范化路径展示 | ✅ |
| 4 | 重定向到 `/dev/null` | `echo test >/dev/null` | 通过 exit 0 | exit 0, 329ms | ✅ |
| 5 | 引号内危险子串 | `echo "rm -rf /"` | 通过（不误杀） | exit 0, 输出 `rm -rf /` | ✅ |
| 6 | 简单 echo | `echo hello` | 通过 exit 0 | exit 0, 输出 `hello` | ✅ |
| 7 | 大写 `>NUL` | `echo test >NUL` | JCC9005 拦截 | JCC9005 拦截 | ✅ |
| 8 | 保留设备名 `>con` | `echo test >con` | JCC9005 拦截 | JCC9005 拦截 | ✅ |
| 9 | 工作区内重定向 | `echo test > ./tmp_output.txt` | 通过 exit 0 | exit 0, 309ms | ✅ |
| 10 | 未闭合单引号 | `echo 'hello world` | JCC9010 拦截 | JCC9010 拦截 | ✅ |
| 11 | 嵌套引号 | `echo "it's a test"` | 通过 exit 0 | exit 0, 输出 `it's a test` | ✅ |
| 12 | 父目录重定向 | `echo test > ../outside.txt` | JCC9009 拦截 | JCC9009 拦截 + 规范化 `D:\project\outside.txt` | ✅ |

**12/12 通过**。覆盖：保留设备名（3 变体）、未闭合引号（单/双）、重定向白名单（工作区内/外/父目录/`/dev/null`）、AC 分位置不误杀、嵌套引号正常通过。

测试产物 `tmp_output.txt` 已归档到 `.xxx/tmp_output.txt.20260917.del`。

### Gap 修复：DangerousCommandNode（2026-09-17）

手动验证发现 git 全局参数黑灯（commit `5a8e92dcf`）存在 gap：`CommandDangerClassifier` 正确分类 `git -c` 为 Dangerous，但 `CommandInterceptionDispatcher` 内唯一用分类器的 `CmdIndirectCallGuard` 只处理 `cmd /c`/`powershell -Command` 间接调用形式，**直接 `git -c` 在 `CanHandle` 阶段被跳过**。

**修复**：BashDefense 链首位新增 `DangerousCommandNode`，委托 `ICommandDangerClassifier` 分类，Dangerous 级直接拒绝（JCC9011）。

| # | 场景 | 命令 | 修复前 | 修复后 | 结果 |
|---|------|------|--------|--------|------|
| 13 | `git -c` 注入 | `git -c core.sshCommand=rm status` | ❌ 放行 | JCC9011 拦截 | ✅ |
| 14 | `git --exec-path` | `git --exec-path=/tmp/evil status` | ❌ 放行 | JCC9011 拦截 | ✅ |
| 15 | `git --config-env` | `git --config-env=foo=bar status` | ❌ 放行 | JCC9011 拦截 | ✅ |
| 16 | 正常 git 不误杀 | `git status` | ✅ 通过 | ✅ 通过 | ✅ |
| 17 | echo 不误杀 | `echo hello` | ✅ 通过 | ✅ 通过 | ✅ |

9 个单元测试 + 466 全量测试通过。commit `8490c02a0`。

### ArgvHash 两轮确认链路打通 + MtpPerturbation 自适应触发生效（2026-09-17）

手动验证发现 3 个 gap 并全部修复：

| Gap | 根因 | 修复 | commit |
|-----|------|------|--------|
| 1. 两轮链路断裂 | bash MCP 工具无 confirmed_command/argv_hash 参数，中间件 Begin() 恒传 null | 加 MCP 参数 → ShellPipelineContext → 中间件 | `7f9b57718` |
| 2. 确认码 # 前缀不匹配 | ComputeArgvHash 返回无 #，显示带 #，ValidateArgvHash 直接比较 | TrimStart('#') 剥离前缀 | `7f9b57718` |
| 3. 自适应触发不生效 | ShouldTriggerAdaptive=true 时只 log 不 action | MtpPerturbationNode 加 IsAdaptiveTriggered 标志，中间件读取 | `7f9b57718` |

**exe 手动验证（IsAntiCharLossConfirm=true）**：

| # | 场景 | 结果 |
|---|------|------|
| 18 | 第一轮 echo hello → 拒绝+确认码 #6D9387 | ✅ JCC9006 |
| 19 | 第二轮 echo hello + 正确 hash → 通过 | ✅ exit 0 |
| 20 | 错误 hash → 拒绝 | ✅ JCC9008 |
| 21 | 错误 confirmed_command → 拒绝 | ✅ JCC9007 |
| 22 | hash 无 # 前缀 → 通过 | ✅ TrimStart 生效 |

**MtpPerturbation 自适应触发**：单元测试验证连续3次异常触发 IsAdaptiveTriggered=true。CLI 模式下状态不跨进程持久化（单次进程），MCP server 长运行模式下有效。

466 + 259 测试全部通过。

## 替代方案

1. **关掉 MTP** — 放弃推理吞吐，不可行。MTP 是供应商侧优化，Agent 无法控制
2. **仅 PostToolUse 补救** — 已否决。补救≠预防，不可撤回操作一旦执行副作用已固化
3. **仅手动开关 AntiCharLossConfirm** — 已否决。扰动是间歇性的，手动开关无法及时响应
4. **修改 ShellCommand.Parse 为严格模式** — 已否决。Parse 被 30+ 调用方依赖，改为 throw 会破坏现有调用链。改用独立 StrictParseNode 在防御链首位拦截
5. **AC 保持全串扫描，增加引号感知** — 已否决。AC 引号感知复杂度高且脆弱，分位置匹配（CommandName + Arguments）更直接可靠

## 后果

- 正面：
  - MTP 扰动导致的 `>nul`、未闭合引号、路径逃逸等在执行前被拦截
  - `echo "rm -rf /"` 不再被误判为危险命令，消除误杀
  - 扰动统计自适应触发，无需手动开关
  - node 化架构可随时组装新防御设备，扩展性好
- 负面：
  - 防御链增加每次 bash 调用的前置检查开销（微秒级，可忽略）
  - `RequireArgvHash` 在 AntiCharLossConfirm 模式下需要两轮交互，增加延迟
  - 分位置 AC 匹配放弃了 `AhoCorasick` 一次扫描的能力，改为线性遍历组合表（组合数量少，性能影响可忽略）
- 中性：
  - `GlobalTwoPhaseConfirmGuard` 和 `NullRedirectTwoPhaseGuard` 已归档到 `.xxx/`，逻辑由 BashDefense 链替代

## 相关

- 实施计划：[docs/plan/safety/SAF001-mtp-perturbation-defense-plan.md](../plan/safety/SAF001-mtp-perturbation-defense-plan.md)
- 相关 ADR：[0034](0034-command-interception-layered.md) 命令拦截分层 · [0036](0036-defense-in-depth-l1-l10.md) 纵深防御 L1-L10 · [0047](0047-unified-danger-level-classification.md) 统一危险指令分级 · [0104](0104-write-defense-node-chain.md) WriteDefense node 链
