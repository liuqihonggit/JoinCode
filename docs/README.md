# docs/ 文档体系 — DSH 四状态决策记录

> 📍 **本文件是 docs/ 的唯一总入口**。所有 AI 和人类阅读 docs/ 文档前,先读本文件。

---

## 目录结构

> **2026-10-11 DSH 四状态分类**: 全局文档按维护义务轻重分为 implemented/archived/proposed/rejected 四种状态。
> 域级文档已迁移到 `modules/<domain>/docs/`。

### 四状态体系（DSH 决策记录状态分类）

| 状态目录 | 含义 | 维护义务 | 文档数 |
|----------|------|----------|--------|
| [`implemented/`](implemented/) | 当前有效的决策记录 | **活跃维护** — 每个路径/符号/默认值/链接都形成维护义务，需持续同步更新 | 151 |
| [`archived/`](archived/) | 已实施但不再作为活跃指导 | **冻结只读** — 永久冻结，只作历史背景，不得因后续变化而更新 | 19 |
| [`proposed/`](proposed/) | 提议中、待讨论 | **待转化** — 讨论后转为 implemented 或 rejected | 7 |
| `rejected/` | 被否决的方案 | **仅保留能防止重蹈覆辙的** — 否则完整删除 | 0 |

### 状态转换规则

```
proposed ──讨论通过──→ implemented ──不再活跃──→ archived
   │                       │
   │                       └──被取代──→ archived (superseded)
   │
   └──讨论否决──→ rejected (仅保留有教育价值的)
```

- **proposed 绝不直接归档** — 过时提案应转为 rejected
- **只有 implemented 能归档** — archived 是 implemented 的终态
- **archived 永久冻结** — 不得因后续的包重命名、行为变化或格式规则而更新

### 各状态子目录结构

```
implemented/
  adr/        — accepted ADR (114 个)
  AI交互/     — git 交互 ADR (10 个)
  design/     — 当前有效设计文档 (3 个)
  plan/       — 已实施计划 (1 个)
  refactor/   — 当前有效重构文档 (6 个)
  task/       — 当前有效任务文档 (17 个)

archived/
  adr/        — superseded ADR (6 个)
  design/     — 陈旧/已完成设计文档 (2 个)
  fix/        — 已完成修复记录 (4 个)
  plan/       — 已完成计划 (3 个)
  refactor/   — 已完成重构记录 (1 个)
  task/       — 已完成任务记录 (3 个)

proposed/
  adr/        — proposed ADR (2 个)
  design/     — 待决策设计文档 (3 个)
  task/       — 待实施任务 (2 个)
```

### 域级文档（已迁移到 modules/）

域级设计、任务、计划、修复、重构、交接文档已按就近原则迁移到各功能域:

```
modules/<domain>/docs/
  design/     — 该域设计文档
  task/       — 该域任务清单
  plan/       — 该域执行计划
  bugfix/     — 该域 bug 修复记录
  fix/        — 该域缺陷修复记录
  handoff/    — 该域交接文档
  refactor/   — 该域重构记录
  reference/  — 该域参考文档
```

16 个功能域: `generators` `collections` `persistence` `async_primitives` `abstractions`
`guard` `storage` `plugins` `mcp` `llm` `cognition` `perception` `services` `composition`
`apps` `tools`

> 迁移计划详见 [DSG040](proposed/design/DSG040-colocation-module-isolation-plan.md) 和
> [ADR 0137](implemented/adr/data_core_selection/0137-colocation-module-isolation.md)。

---

## 命名规范

1. **目录名**: 全小写 + 下划线, 禁止中文、禁止连字符(`-`)
2. **文件名**: 可中文(任务/计划文档用中文便于理解), 英文文件名用 kebab-case
3. **ADR 文件名**: `NNNN-kebab-case-title.md`(四位序号, ADR 专属)

---

## 文档状态标注

每个文档头部应标注当前状态:

```markdown
# 文档标题

> 状态: implemented | archived | proposed | rejected
> 日期: YYYY-MM-DD
```

### ADR 状态映射

| ADR 原状态 | DSH 状态 | 说明 |
|-----------|----------|------|
| accepted | implemented | 当前有效的决策记录 |
| superseded | archived | 已被取代, 冻结历史 |
| proposed | proposed | 提议中, 待讨论 |
| deprecated | archived | 已废弃, 冻结历史 |

---

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-10-11 | DSH 四状态分类: 全局文档按 implemented/archived/proposed/rejected 归类, 旧目录删除 |
| 2026-10-11 | 域级文档就近迁移到 modules/<domain>/docs/(185 个文档) |
| 2026-09-15 | 初始规范: 全面重新归类为 9 个一级子目录 |
