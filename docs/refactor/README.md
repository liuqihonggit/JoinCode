# refactor/ — 重构与迁移记录

> 📍 **导航**: [docs/](../README.md) › refactor/ | **前置**: [adr/](../adr/README.md)(决策依据)
> 🔗 **上游索引**: [docs/README.md](../README.md) — 新增/删除文档后须同步更新

记录"怎么改"。重构执行记录和迁移记录,按需查阅。

## 文档列表

| 编号 | 文件 | 标题 | 类型 |
|------|------|------|------|
| REF001 | [REF001-0012-two-itoolhandler-interfaces.md](REF001-0012-two-itoolhandler-interfaces.md) | 双 IToolHandler 接口不合并 | 重构决策 |
| REF002 | [REF002-archive-dead-code.md](REF002-archive-dead-code.md) | 死代码归档记录 | 清理 |
| REF003 | [REF003-file-watcher-unification.md](REF003-file-watcher-unification.md) | 文件监控全面 Actor 化统一 | 重构 |
| REF004 | [REF004-foreach-to-dict-report.md](REF004-foreach-to-dict-report.md) | foreach / FirstOrDefault 转字典优化报告 | 优化 |
| REF005 | [REF005-jcc-path-consolidation.md](REF005-jcc-path-consolidation.md) | ~/.jcc/ 路径统一整理计划 | 迁移 |
| REF006 | [REF006-lock-to-asynclock.md](REF006-lock-to-asynclock.md) | lock → AsyncLock 全量迁移计划 | 迁移 |
| REF007 | [REF007-lock-to-channel-pipeline-plan.md](REF007-lock-to-channel-pipeline-plan.md) | 锁 → 管道通讯重构方案 | 重构 |
| REF008 | [REF008-field-merge-refactor-tasks.md](REF008-field-merge-refactor-tasks.md) | 字段合并重构任务 | 重构 |
