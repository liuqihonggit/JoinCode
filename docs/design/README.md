# design/ — 跨域技术设计(全局)

> 📍 **导航**: [docs/](../README.md) › design/ | **前置**: [adr/](../adr/README.md)(决策依据)
> 🔗 **上游索引**: [docs/README.md](../README.md) — 新增/删除文档后须同步更新

> **2026-10-11 文档就近迁移**: 域级设计文档已迁移到 `modules/<domain>/docs/design/`。
> 本目录仅保留全局性设计文档(跨域架构索引/扁平化/韧性/就近原则)。

## 全局设计文档

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG001 | [DSG001-architecture-index.md](DSG001-architecture-index.md) | 项目架构索引(组件依赖图/详情表/中间件管道) |
| DSG002 | [DSG002-technical-details.md](DSG002-technical-details.md) | 技术要点(容错/前缀缓存/循环干预/并行加载) |
| DSG004 | [DSG004-flatten-restructure-plan.md](DSG004-flatten-restructure-plan.md) | 文件夹扁平化重组方案 |
| DSG005 | [DSG005-flatten-restructure-phase0-research.md](DSG005-flatten-restructure-phase0-research.md) | 阶段0 前置调研 |
| DSG013 | [DSG013-UnifiedResilienceArchitecture.md](DSG013-UnifiedResilienceArchitecture.md) | 统一韧性架构设计 |
| DSG023 | [DSG023-worktree-merge-permission-system-questions.md](DSG023-worktree-merge-permission-system-questions.md) | Worktree 合并权限系统问题 |
| DSG040 | [DSG040-colocation-module-isolation-plan.md](DSG040-colocation-module-isolation-plan.md) | 就近原则功能域隔离迁移计划(16域+文档就近+依赖隔离) |
