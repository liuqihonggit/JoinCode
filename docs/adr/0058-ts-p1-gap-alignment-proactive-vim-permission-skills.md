# 0058. TS 原版 P1 缺口补齐 — Proactive + Vim + Permission LLM + Skills

- 状态：accepted（P1.1+P1.3+P1.4 完成，P1.2 Vim 用户取消）
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0057](0057-ts-p0-gap-alignment-lsp-analytics.md)（P0 缺口补齐）
- 背景：jcc vs Claude Code TS 原版 P1 差异分析

## 背景

P0 缺口（LSP + Analytics）已全部完成（ADR 0057 accepted）。P1 差异分析识别出 4 个真实缺口，另有 2 个领域（SSH/Coordinator）jcc 已超越 TS，无需补齐。

### 已覆盖（jcc 领先）

- **SSH 会话管理**：jcc 有完整 `SshSessionManager` + `SshPortForwardManager` + 4 个模型 + 抽象接口，TS 仅用 SSH 作为传输协议
- **Coordinator**：jcc 有 `AgentCoordinator`（800+ 行）+ Fork/Team/Swarm 三子系统，TS 仅单一 worker 模型

### 4 个真实缺口

| 编号 | 缺口 | jcc 现状 | TS 现状 | 价值 | 复杂度 |
|------|------|----------|---------|------|--------|
| P1.1 | Proactive 主动模式 | 骨架（状态服务+提示词+Sleep 工具） | 完整（tick 调度+终端焦点+自主循环集成） | 高 | 高 |
| P1.2 | Vim 完整引擎 | 存根（72 行，仅模式切换） | 完整（operators/motions/textObjects/transitions 5 子系统） | 中 | 高 |
| P1.3 | Permission LLM 分类器 | 规则驱动（181 行正则） | 两阶段 LLM 驱动（1300+ 行） | 中 | 中 |
| P1.4 | Skills 内置技能 | 9 个内置技能 | 13+ 个内置技能 | 中 | 低 |

## 决策（提议）

按价值/复杂度排序，渐进式补齐：

### P1.1 Proactive 主动模式（最高优先级）

**目标**：实现 tick 调度循环 + 终端焦点检测，使 AI 能持续自主工作

**改动范围**：
1. `app/JoinCode/Services/UserExperience/` — 扩展 `ProactiveStateService`
   - 新增 `ProactiveTickScheduler` — tick 调度器（`getNextTickAt` 逻辑）
   - 新增 `TerminalFocusDetector` — 终端焦点状态检测（跨平台）
2. `core/execution/Brain/` — 集成 proactive tick 到主循环
   - 消息队列集成（系统生成命令路由）
   - 压缩时 proactive 模式处理

**AOT 约束**：
- 终端焦点检测用 OS 原生 API（Win32 `GetForegroundWindow` / Unix `/proc` 或 `tcgetpgrp`）
- tick 调度用 `Timer` + `CancellationToken`，不引入第三方库

### P1.2 Vim 完整引擎

**目标**：补完整 vim 键绑定（operators + motions + textObjects）

**改动范围**：
1. `app/JoinCode/Cli/Vim/` — 扩展 `VimEngine.cs`
   - 新增 `VimOperators.cs` — d/y/c/p 等操作符
   - 新增 `VimMotions.cs` — hjkl/w/b/e/0/$ 等移动
   - 新增 `VimTextObjects.cs` — aw/iw/ap 等文本对象
   - 新增 `VimTransitions.cs` — 状态转换机
   - 扩展 `VimEngine.cs` — 实现寄存器/标记/宏

**AOT 约束**：纯 C# 实现，无外部依赖

### P1.3 Permission LLM 分类器

**目标**：扩展现有 `AutoModeClassifier` 增加 LLM 侧查询路径

**改动范围**：
1. `core/safety/Guard/src/Security/Services/` — 扩展 `AutoModeClassifier`
   - 新增 `LlmAutoModeClassifier.cs` — 两阶段 LLM 分类器
   - Stage 1：快速分类（规则优先，命中则返回）
   - Stage 2：深度分类（LLM 侧查询，处理复杂命令组合）
   - 新增分类结果转储（调试用 req/res JSON）

**AOT 约束**：
- LLM 侧查询复用现有 `ILlmQueryService` + `JsonContext`
- 禁止 `dynamic`，用源码生成器生成请求/响应类型

### P1.4 Skills 内置技能补充

**目标**：补齐 TS 有但 jcc 缺少的内置技能

**改动范围**：
1. `core/execution/Hands/src/Skills/BuiltIn/` — 新增技能
   - `updateConfig` — 配置更新技能（用户常用）
   - `keybindings` — 键绑定管理技能（用户常用）
   - 其余按需（loremIpsum/dream/scheduleRemoteAgents/claudeApi/claudeInChrome/runSkillGenerator）

**AOT 约束**：技能是提示词+执行逻辑，架构已就绪，无额外约束

## 替代方案

### 方案 A：只做 P1.1 Proactive（放弃其余）

- **放弃原因**：Proactive 是自主代理核心能力，价值最高；其余 3 项可后续补
- **适用场景**：若时间有限，优先补 Proactive

### 方案 B：跳过 P1.2 Vim（放弃）

- **放弃原因**：Vim 完整引擎复杂度高（5 子系统），CLI 用户中 vim 用户占比有限
- **适用场景**：若用户群体以非 vim 用户为主

### 方案 C：用第三方 Vim 库（放弃）

- **放弃原因**：无成熟 NativeAOT 兼容的 C# vim 库
- **替代**：自实现，分阶段补 motions → operators → textObjects → 宏

## 后果

- **正面**：
  - P1.1：AI 获得自主循环能力，可持续工作不需人工输入
  - P1.2：完整 vim 编辑体验，吸引 vim 用户
  - P1.3：LLM 分类器处理复杂命令组合，安全性提升
  - P1.4：内置技能补齐，功能对齐 TS
- **负面**：
  - P1.1：终端焦点检测跨平台实现复杂（Win32/Unix 差异）
  - P1.2：vim 引擎 5 子系统工作量大
  - P1.3：LLM 侧查询增加延迟和 token 消耗
  - P1.4：部分技能（dream/scheduleRemoteAgents）依赖 KAIROS 特性，可能不适用
- **中性**：
  - 4 项改动独立，可分 4 个 PR 合并

## 实现顺序

```
P1.1 Proactive → P1.4 Skills（低复杂度快速完成）→ P1.3 Permission LLM → P1.2 Vim
```

每项遵循：ADR → 红测试 → 实现 → 编译 → 绿测试 → git 提交

## 验证标准

| 项 | 验证标准 |
|----|----------|
| P1.1 Proactive | tick 调度器按间隔触发，终端失焦时暂停 tick |
| P1.2 Vim | `dw` 删除单词，`yy` 复制行，`p` 粘贴，`ciw` 替换单词内文本 |
| P1.3 Permission LLM | 复杂命令组合（如 `rm -rf && curl`）通过 LLM 分类为 Dangerous |
| P1.4 Skills | `updateConfig` 技能可更新 settings.json 字段 |
