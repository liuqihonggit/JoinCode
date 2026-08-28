# ADR — 架构决策记录

> **ADR (Architecture Decision Records)** 记录项目中重要的架构决策：**为什么**选 A 而不选 B，以及后果是什么。

## 为什么需要 ADR

项目已有 `docs/design/`（怎么实现）、`docs/plans/`（什么时候做）、`docs/tasks/`（做什么），但缺少**决策文档**（为什么这样选）。决策散落在 `AGENTS.md`、各 design 文档、代码注释中，难以回溯。

ADR 用固定格式收编这些决策，形成不可变的历史记录。

## 目录规范

- 文件名：`NNNN-kebab-case-title.md`（NNNN=四位序号，从 0001 开始）
- 语言：简体中文
- 不可变：决策一经 accepted 不再修改内容，只改状态（superseded/deprecated）

## 模板

```markdown
# NNNN. 标题

- 状态：proposed | accepted | superseded by NNNN | deprecated
- 日期：YYYY-MM-DD
- 决策者：

## 背景

（为什么需要这个决策，当时面临什么问题）

## 决策

（最终选了什么）

## 替代方案

（考虑过但没选的方案，及放弃原因）

## 后果

- 正面：
- 负面：
- 中性：
```

## 与其他文档的关系

| 文档 | 职责 | 示例 |
|------|------|------|
| `docs/adr/` | **为什么**这样决策 | 为什么用 slnx 隔离而非单 sln |
| `docs/design/` | **怎么**实现 | 七层 slnx 的具体依赖链和编译顺序 |
| `docs/plans/` | **什么时候**做 | 重构执行计划和里程碑 |
| `docs/tasks/` | **做什么** | 具体任务清单 |

ADR 引用 design/plans，但不重复其内容。

## 粒度策略

本项目采用**架构级 + 组件策略级**两层：
- 架构级（0001-0012）：跨模块、影响全局的决策
- 组件策略级（0013-0024）：组件设计风格、工作方法论、反模式禁令

函数级决策留在代码注释或 design 文档中。

## 索引

### 架构级（0001-0012）

| 编号 | 标题 | 状态 |
|------|------|------|
| 0001 | 七层 slnx 隔离架构 | accepted |
| 0002 | NativeAOT + 禁用微软 AI 包 | accepted |
| 0003 | rebase 而非 merge | accepted |
| 0004 | 配置大于代码 — 模态能力显式注册 | accepted |
| 0005 | 文件驱动界面 | accepted |
| 0006 | 双层 TDD | accepted |
| 0007 | 渐进式开发方法 | accepted |
| 0008 | .xxx 归档而非删除 | accepted |
| 0009 | MCP Streamable HTTP 2025-11-25 | accepted |
| 0010 | GlobalUsings 统一管理 | accepted |
| 0011 | 数据容器 AOT+GC 选型 | accepted |
| 0012 | 双 IToolHandler 接口不合并 | superseded by 0025 |

### 组件策略级（0013-0024）

| 编号 | 标题 | 状态 | AGENTS.md 对应位置 |
|------|------|------|-------------------|
| 0013 | 超图与 DAG 分工 | accepted | 六项架构规则·规则1 |
| 0014 | MCP 工具覆盖原则 | accepted | 六项架构规则·规则2 |
| 0015 | 配置热重载双变量切换 | accepted | 六项架构规则·规则3 |
| 0016 | 参数传接口不传属性 | accepted | 六项架构规则·规则5 |
| 0017 | 归纳性重构不放弃 | accepted | 六项架构规则·规则6 |
| 0018 | 循环检测器状态机风格 | accepted | 六项架构规则·规则8 |
| 0019 | 枚举 + EnumValue + 源码生成器 | accepted | 封装要求·枚举扩展 |
| 0020 | 封装要求 | accepted | 封装要求 |
| 0021 | E2E 脚本 Mode 计算属性 | accepted | E2E 测试脚本模式规范 |
| 0022 | C# AST CLI 优先于正则 | accepted | 脚本语言优先级 |
| 0023 | 减法思维优先 | accepted | 反例4·加法思维 |
| 0024 | 治标不治本禁令 | accepted | 反例3·治标不治本 |
| 0025 | 归档 IMcpProtocolHandler 死接口 | accepted | 规则4·取代0012 |
