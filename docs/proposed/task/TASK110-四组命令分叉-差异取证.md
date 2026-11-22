# TASK110 — `/init` `/memory` `/loop` `/branch+/fork` 命令分叉（TASK103 §2.3 #43 取证）

> 状态：**四组均裁决保留本仓设计 / 关闭** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#43 / TASK071-C3**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

TASK071:119 原文四项属实，但逐项回一手源码后，**没有一项是"断链/静默失效"**：三项是产品身份与命令 UX 分叉，一项有等价能力在别的命令上。无人值守不改命令契约与输出物。

| 命令 | 基准 | 本仓 | 裁决 |
|---|---|---|---|
| `/init` | 写 `CLAUDE.md` + `.claude/` 生态 | 写 `.jcc/project_rules.md` + `.jcc/` 配置骨架 | 保留：换文件名会破坏本仓配置唯一源 |
| `/memory` | 编辑器打开 CLAUDE.md 一处 | 结构化 DB 管理 8 个子命令，含 edit/CLAUDE.md | 保留：本仓是超集 |
| `/loop` | 内置技能 → CronCreate 定时调度 + 立即跑一次 | 内置技能 → 内联循环步骤机 | 保留：cron 能力由 `/goal --cron` + cron 工具提供 |
| `/branch` + `/fork` | 单命令（别名 fork）做 transcript 复制 + resume | `/fork` 做等价复制；`/branch` 四个子动作诚实报"未实现" | 保留：核心能力已由 `/fork` 覆盖，分支树需新子系统 |

---

## 1. 四问回答

### 问1 专家隐性实践
斜杠命令名是**用户契约**，不是内部函数名；同一产品里同名命令语义漂移是严重 bug，但**两个不同产品之间**同名而目标不同是常态。裁决原则是"本仓是否已有等价能力 + 改名的破坏面"，而不是逐字对齐输出文件名。对"未实现"的动作，诚实拒绝优于假装成功（本仓 `/branch` 注释明确写这条）。

### 问2 原始素材（一手）
- 基准 `/init`：`src/commands/init.ts:226-256`，`type:'prompt'`，注入 `OLD_INIT_PROMPT`/`NEW_INIT_PROMPT`（`:6-224`），由模型写 `CLAUDE.md`（`:95`）、`CLAUDE.local.md`（`:137`）、`.claude/skills/*/SKILL.md`（`:171`）、`.claude/settings[.local].json` hooks（`:198`）；不接位置参数。
- 基准 `/memory`：`src/commands/memory/index.ts:3-8` + `memory.tsx:14-89`，只做"清缓存 → 找到/创建文件 → `$VISUAL/$EDITOR` 打开"（`:83-89`、`:33-55`），**不调用** `findRelevantMemories`（自动召回在附件预取里，见 TASK109）。
- 基准 `/loop`：`src/skills/bundled/loop.ts:74-92`，`[interval] <prompt>` 默认 10m（`:9,:30-44`），提示词要求调 `CronCreate` 并立即执行一次（`:62-67`），门控 `isKairosCronEnabled`。
- 基准 `/branch`：`src/commands/branch/index.ts:4-12`（`aliases:['fork']` 仅无 `FORK_SUBAGENT` 时）+ `branch.ts:61-173/222-296`（复制 transcript、改 sessionId/parentUuid、写 JSONL、标题 "(Branch N)"、resume）。
- 本仓 `/init`：`modules/composition/slash/guard/config/InitCommand.cs:8-29`，`quick` 模式写 `.jcc/project_rules.md:85` + Hub 拉取供应商配置写 `~/.jcc/settings.json:120-145`，AI 模式把分析提示词交给 `ChatService.SendMessageAsync:60-62`（要求写 `.jcc/project_rules.md:216`）。
- 本仓 `/memory`：`modules/composition/slash/agents/agent/MemoryCommand.cs:6-8/24-53`，子命令 edit/open/add/search/db/stats/health/cleanup；候选文件含 AGENTS.md/CLAUDE.md（`:206-227`）；`add` 走 `MemoryManagementService.AddMemoryAsync:290`，search/db 走 `ScanMemoriesAsync:328/360`。
- 本仓 `/loop`：`modules/cognition/hands_core/skills/built_in/LoopSkill.cs:7-30`，`task` 必填 + `condition`/`maxIterations`（默认 10，1-100），产出 4 步内联提示词与 `SkillStepType.Loop`，非 cron。
- 本仓 `/fork`：`modules/composition/slash/brain/session/ForkCommand.cs:6-107`，`ITranscriptService` 加载 → 过滤 sidechain → `SessionIdFactory.CreateFork` → 克隆消息 → `AppendEntriesAsync` 落盘 → 提示 `/resume`。本仓 `/branch`：`BranchCommand.cs:10-82`，list/create/switch/delete 四个分支全部只打印"分支管理服务尚未实现"。

### 问3 具体方法与工具
两侧一手命令文件逐行读 + grep 交叉调用点（确认基准 `/memory` 与自动召回无关、本仓 `/loop` 不走 CronToolHandlers）；对 `/branch` 额外追注册表判定别名冲突是否造成功能劫持：`ChatCommandRegistry.cs:29-38` → `CategorizedRegistry.RegisterAlias:54-56` → `MapRegistry.RegisterAlias:287-297`（**不覆盖已存在键**），因此 `ForkCommand` 的 `Aliases=["branch"]` 不会抢走 `BranchCommand` 的规范名，注册顺序至多产生一条 warning，无功能影响。

### 问4 独立验证来源
基准 `/branch` 别名仅在 `!FORK_SUBAGENT` 时启用（`branch/index.ts:8`），开启后 `/fork` 指向分叉子代理（`tools/AgentTool/forkSubagent.ts:60-198`），说明基准自己也有同名多义；本仓 `/goal --cron`（`GoalCommand.cs:229-252` 写 `ICronTaskStore`）与 `CronToolHandlers.cs:5-8` 证明定时调度能力存在，`/loop` 不是唯一载体。

---

## 2. 逐项判定与不改动理由

1. **`/init` 输出物**：`.jcc/project_rules.md` 是本仓 prompt/config 的唯一项目规则位置，`.jcc/` 还承载 settings/current template。改写成 `CLAUDE.md` 要么双写（二义性：谁覆盖谁），要么迁移全仓配置约定（远超本项）。基准产物另有 `.claude/skills`/hooks 生态，本仓 skills 走 skill 引擎、hooks 走 guard 管道，架构层不同。**保留**。
2. **`/memory`**：基准只编辑一个 Markdown 文件；本仓 8 个子命令覆盖增/查/去重/健康/清理，并且 `edit` 也接受 CLAUDE.md 等文件（`:206-227`）。**功能超集，保留**；无证据说明缺"打开编辑器"能力。
3. **`/loop`**：基准内联循环 + cron 是"立即执行 + 定时重复"组合；本仓内联循环由技能步骤机提供，定时任务由 `/goal --cron` + `cron_create` 等工具提供。把 `/loop` 改成 cron 会改变现有技能用户行为，且需要新增"立即执行一次"的调度语义。**能力不缺，入口不同，保留**。
4. **`/branch` + `/fork`**：基准单命令的实质是 transcript 复制 + 新 session + resume —— 本仓 `/fork` 已覆盖；本仓 `/branch` 的 list/switch/delete 需要 `BranchManager`（树形分支状态 + 切换会话上下文）这一新子系统，当前是诚实拒绝，不存在"假成功"。`ForkCommand` 的 `branch` 别名与 `BranchCommand` 规范名相撞，但注册表语义保证规范名/先注册者不被别名劫持。**保留**。

---

## 3. 验收（取证项，无代码改动）

- 四组命令两侧入口与输出物均以 `file:line` 记录于 §1，可直接复核。
- 别名冲突按源码链路确认无功能影响：`ChatCommandRegistry.Register` → `CategorizedRegistry.RegisterAlias` → `MapRegistry.RegisterAlias`（不覆盖既有键）。
- cron 能力存在性：`/goal --cron` 与 `CronToolHandlers` 两处独立消费点。
- 总账 #43 划掉并指向本文档。
