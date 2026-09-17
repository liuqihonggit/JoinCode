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

- 实施计划：[docs/plan/safety/mtp-perturbation-defense-plan.md](../plan/safety/mtp-perturbation-defense-plan.md)
- 相关 ADR：[0034](0034-command-interception-layered.md) 命令拦截分层 · [0036](0036-defense-in-depth-l1-l10.md) 纵深防御 L1-L10 · [0047](0047-unified-danger-level-classification.md) 统一危险指令分级 · [0104](0104-write-defense-node-chain.md) WriteDefense node 链
