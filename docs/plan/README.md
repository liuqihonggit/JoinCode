# plan/ — 执行计划(什么时候做)

> 📍 **导航**: [docs/](../README.md) › plan/ | **前置**: [adr/](../adr/README.md)(决策) → [design/](../design/README.md)(设计)
> 🔗 **上游索引**: [docs/README.md](../README.md) — 新增/删除文档后须同步更新

执行计划、里程碑、改造方案。回答"什么时候做"。

## 散落计划

| 编号 | 文件 | 标题 |
|------|------|------|
| PLA001 | [PLA001-Workflow断点续跑工作计划.md](PLA001-Workflow断点续跑工作计划.md) | Workflow 断点续跑工作计划 |
| PLA002 | [PLA002-gh-rest-api-rewrite-plan.md](PLA002-gh-rest-api-rewrite-plan.md) | gh_* MCP 工具重写为 REST API 直调 |
| PLA003 | [PLA003-using改造工作计划.md](PLA003-using改造工作计划.md) | using 改造工作计划 |
| PLA004 | [PLA004-强迫症扁平化方案.md](PLA004-强迫症扁平化方案.md) | 命名规范 + 同名层级消除 |
| PLA005 | [PLA005-文件夹整理改革方案.md](PLA005-文件夹整理改革方案.md) | 从领域驱动到功能驱动 |
| PLA006 | [PLA006-文件夹整理迁移映射表.md](PLA006-文件夹整理迁移映射表.md) | 文件夹整理迁移映射表 |

## 子目录

| 目录 | 主题 | 文档数 | 说明 |
|------|------|--------|------|
| [agent/](agent/) | 子代理 | 7 | Agent 架构、继承树、中断、防冲突 |
| [mcp/](mcp/) | MCP 工具测试 | 12+README | 按 ToolCategory 分组的测试计划(已有 [README](mcp/README.md)) |
| [plugin/](plugin/) | 插件系统 | 6 | Cordis 框架、Entity 资源化、万物皆插件 |
| [refactor/](refactor/) | 重构计划 | 12 | 状态机、架构选型、0-GC、Spawn 管道 |
| [safety/](safety/) | 安全防御 | 4 | 熵减检测器、纵深防御、超时、重试 |
| [tui/](tui/) | TUI | 3 | 交互规格、架构重构、验收修复 |
