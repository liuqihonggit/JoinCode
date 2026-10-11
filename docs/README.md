# docs/ 文档体系 — 导航与规范

> 📍 **本文件是 docs/ 的唯一总入口**。所有 AI 和人类阅读 docs/ 文档前,先读本文件。
> 修改任何文档后,必须按 [索引一致性维护机制](#索引一致性维护机制) 同步更新上游索引。

---

## 目录结构

> **2026-10-11 文档就近迁移**: 域级文档已迁移到 `modules/<domain>/docs/`。
> `docs/` 仅保留全局文档(ADR + 跨域设计 + 全局参考 + 全局任务)。

### 全局文档(保留在 docs/)

| 目录 | 职责 | 文档数 | 说明 |
|------|------|--------|------|
| [`adr/`](adr/README.md) | 架构决策记录 | 137 | 全局,不可变,双索引 |
| [`design/`](design/) | 跨域设计文档 | 9 | 全局性设计(DSG001/004/005/013/023/040 等) |
| [`reference/`](reference/) | 全局参考 | 3 | 错误编码、代码审查报告(按需查阅) |
| [`reports/`](reports/) | 全局报告 | 1 | dispose 超时违规报告 |
| `zh-CN/` | 项目中文说明 | 1 | 非开发文档,保留原位 |

### 域级文档(已迁移到 modules/)

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

> 迁移计划详见 [DSG040](design/DSG040-colocation-module-isolation-plan.md) 和
> [ADR 0137](adr/0137-colocation-module-isolation.md)。

---

## 命名规范

1. **目录名**:全小写 + 下划线,禁止中文、禁止连字符(`-`)
   - ✅ `task/gui/`、`plan/refactor/`、`handoff/0080_round3_handoff/`
   - ❌ `修复计划/`、`ai-interaction-docs/`、`avalonia-host/`
2. **文件名**:可中文(任务/计划文档用中文便于理解),但英文文件名用 kebab-case
   - ✅ `启动参数测试记录.md`、`lock-to-asynclock.md`
3. **ADR 文件名**:`NNNN-kebab-case-title.md`(四位序号,ADR 专属)

---

## 两级导航规范

```
docs/README.md          ← 顶层导航(本文件):目录结构 + 规范 + 阅读顺序
├── adr/README.md       ← 子目录索引:按编号 + 按主题双索引
├── design/README.md    ← 子目录索引:按主题分组
├── plan/README.md      ← 子目录索引:含子目录说明
├── task/README.md      ← 子目录索引:含 gui/ 子目录
├── fix/README.md       ← 子目录索引:按时间/轮次
├── refactor/README.md  ← 子目录索引:按主题
├── handoff/README.md   ← 子目录索引:按轮次
└── reference/README.md ← 子目录索引:按用途
```

- **顶层 `docs/README.md`**:只列目录级导航 + 规范,不列单个文档(避免膨胀)
- **子目录 `README.md`**:列本目录所有文档的标题 + 简述 + 链接

---

## 面包屑规范(每个文档头部)

每个 `.md` 文件**第一行标题之后**必须加面包屑块:

```markdown
# 文档标题

> 📍 **导航**: [docs/](../README.md) › [当前目录](README.md) | **前置**: [前置依赖文档](路径)
> 🔗 **上游索引**: [当前目录/README.md](README.md) — 修改本文档后须同步更新此索引
```

| 字段 | 说明 | 示例 |
|------|------|------|
| **导航** | 从 docs/ 根到本文档的路径,用 `›` 分隔 | `docs/ › task/ › 本文档` |
| **前置** | 读本文档前应先读的依赖文档(可选,无依赖则省略) | `[ADR 0081](../adr/0081-xxx.md)` |
| **上游索引** | 引用了本文档的 README 路径(必填,通常是本目录 README) | `[task/README.md](README.md)` |

---

## 索引一致性维护机制

> **核心规则:AI 修改任何文档后,必须检查并同步更新该文档的"上游索引"。**
> 这是防止索引指向过期信息、避免"AI 读到后面文档却不知道要改前面索引"的唯一机制。

### AI 修改文档时的强制流程

1. **修改文档内容(标题/状态/文件名)**:
   - 看文档头部面包屑的 `🔗 上游索引` 字段
   - 同步更新那些 README 中本文档的条目(标题/状态/链接)
   - 如果改了文件名:全局搜索旧文件名,更新所有引用

2. **新增文档**:
   - 在对应子目录的 `README.md` 中添加条目(标题 + 简述 + 链接)
   - 在新文档头部加面包屑块(导航 + 前置 + 上游索引)

3. **移动文档(跨目录)**:
   - 更新面包屑中的导航路径
   - 更新旧目录和新目录的 `README.md`(从旧移除,在新添加)
   - 全局搜索旧路径,更新所有引用

### 判断"是否需要更新上游索引"

| 修改类型 | 需更新上游索引? | 原因 |
|----------|-----------------|------|
| 改文档正文内容 | ❌ 否 | 索引只记录标题/简述/链接,不记录正文 |
| 改文档标题(第一行) | ✅ 是 | 索引中显示标题 |
| 改文档状态(如 ADR proposed→accepted) | ✅ 是 | 索引中显示状态 |
| 改文件名 | ✅ 是 | 索引中的链接会断 |
| 新增文档 | ✅ 是 | 索引需添加新条目 |
| 删除文档 | ✅ 是(移除条目) | 索引需移除条目(文件按 .xxx 归档规则处理) |

---

## 文档模板(新建文档时复制)

```markdown
# 文档标题

> 📍 **导航**: [docs/](../README.md) › [目录名](README.md) | **前置**: [前置文档](路径)
> 🔗 **上游索引**: [目录名/README.md](README.md) — 修改本文档后须同步更新此索引

---

## 背景

(为什么需要这个文档)

## 内容

(正文)

## 相关文档

- [相关文档1](路径) — 关系说明
```

---

## 顶层导航

### adr/ — 架构决策记录(全局)

> 详见 [adr/README.md](adr/README.md)(双索引:按编号 + 按主题)

137 个 ADR,记录"为什么选 A 放弃 B"。不可变,只改状态(superseded/deprecated)。

### design/ — 跨域设计文档(全局)

> 详见 [design/README.md](design/)

9 个全局性设计文档(DSG001/004/005/013/023/040 等)。域级设计文档已迁移到各域 `docs/design/`。

### reference/ — 全局参考

> 详见 [reference/README.md](reference/)

3 个全局参考文档:错误编码、代码审查报告。

### reports/ — 全局报告

1 个全局报告:dispose 超时违规报告。

### zh-CN/ — 项目中文说明(非开发文档)

保留原位,不纳入文档生命周期。详见 [zh-CN/README.md](zh-CN/README.md)。

### 域级文档 — 已迁移到 modules/<domain>/docs/

各功能域的设计、任务、计划、修复、重构、交接文档已迁移到就近位置。
详见各域 `modules/<domain>/README.md` 和 `modules/<domain>/docs/`。

---

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-10-11 | 域级文档就近迁移到 modules/<domain>/docs/(185 个文档),docs/ 仅保留全局文档 |
| 2026-09-15 | 初始规范:全面重新归类为 9 个一级子目录,建立两级导航 + 面包屑 + 索引一致性机制 |
