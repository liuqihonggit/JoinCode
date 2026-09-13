# 0104. 写入防御链提取为公共对象 — 一切皆为 node/插件

- 状态：accepted
- 日期：2026-09-13
- 决策者：项目架构组

## 背景

### 问题起源

文件工具统一防御链 `WriteDefense` 已在 `FileToolHandlers` 中落地（ADR 0103 语义分组后），7 个写入/编辑工具 + FileDelete 复用统一链。但 `NotebookToolHandlers`（`kit/hands/notebook/tool_handlers/`）缺团队密钥检测 + 写前备份 + LSP 通知，且无法复用 `WriteDefense`——因为：

1. `WriteDefense` 的防御步骤是 `FileToolHandlers` 的私有方法，依赖其字段（`_teamMemSecretGuard`、`_fileHistoryService`、`_lspFileSync` 等）
2. `NotebookToolHandlers` 是独立类，不持有这些依赖

### 用户架构愿景

> 一切皆为 node，一切皆为插件。目录按层来处理，最外层是公共对象，最内可以写一些私有的 private/ 文件夹存放私有类型和工具。

核心诉求：
1. **一切皆为 node/插件** — 每个能力（防御链、通知、密钥检测等）都是可插拔的 node/插件
2. **公共对象提取** — `WriteDefense` 从 `FileToolHandlers` 私有方法提取为公共对象，多消费者注入使用
3. **目录按层组织** — 最外层暴露公共对象/接口，最内层 `private/` 存私有类型和工具
4. **全局改造** — 最终推广到所有工具处理器

## 决策

### 1. 提取 `WriteDefenseService` 公共对象

将 `WriteDefense` 链式构建器 + 防御步骤 + `NotifyWriteComplete` 从 `FileToolHandlers` 提取为独立公共对象 `WriteDefenseService`：

```
kit/hands/tool_handlers/handlers/dev_tools/
├── defense/                          ← 防御链公共对象（最外层公共）
│   ├── WriteDefenseService.cs        ← 公共入口，注入 ITeamMemSecretGuard 等依赖
│   ├── WriteDefense.cs               ← 链式构建器
│   ├── WriteDefenseContext.cs        ← 防御上下文
│   └── private/                      ← 私有实现（最内层）
│       ├── WriteDefenseSteps.cs      ← 11 个有名防御步骤（私有方法）
│       └── WriteDefenseDiagnostics.cs← 防御诊断构建（私有）
```

### 2. 依赖注入

`WriteDefenseService` 通过构造函数注入所有防御所需依赖：
- `ISandboxManager?` — 沙箱解析
- `ITeamMemSecretGuard?` — 团队密钥检测
- `IFileStateCache?` — 写前读校验 + 脏写保护
- `IFileHistoryService?` — 写前备份
- `ILspFileSync?` — LSP 文件变更通知
- `ILspDiagnosticProvider?` — LSP 诊断清除
- `IFileWriteListenerRegistry?` — 文件写入监听器
- `ITelemetryService?` — 遥测
- `ISubAgentContextAccessor?` — 子代理上下文（keyword/doctor 校验）
- `IFileSystem` — 文件系统

`FileToolHandlers` 和 `NotebookToolHandlers` 都注入 `WriteDefenseService`，调用统一防御链。

### 3. 目录按层组织原则

| 层 | 位置 | 内容 |
|----|------|------|
| 公共层 | `defense/` | 公共对象、接口、构建器 |
| 私有层 | `defense/private/` | 私有防御步骤、诊断构建、内部类型 |

推广到全局：每个能力模块都按 `公共对象/private/私有实现` 组织。

### 4. 一切皆为 node/插件

`WriteDefenseService` 是第一个按"node/插件"哲学提取的公共对象。后续推广：
- 通知服务 → `NotificationService` node
- 密钥检测 → `SecretGuardService` node
- 备份服务 → `BackupService` node
- 每个 node 可独立注入、替换、测试

## 替代方案

| 方案 | 优点 | 缺点 | 状态 |
|------|------|------|------|
| A. 提取 WriteDefenseService 公共对象 | 统一复用、可注入、符合 node/插件哲学 | 改动量大、需改构造函数+DI | **采用** |
| B. NotebookToolHandlers 内联补防御 | 改动最小 | 两套写法、不统一 | 放弃 |
| C. 不改 NotebookEdit | 零改动 | TS 也缺、防御缺口 | 放弃 |

## 验证

- [ ] `WriteDefenseService` 提取完成
- [ ] `FileToolHandlers` 注入 `WriteDefenseService` 复用
- [ ] `NotebookToolHandlers` 注入 `WriteDefenseService` 补齐密钥检测+备份+LSP通知
- [ ] 目录按层组织（公共 + private/）
- [ ] 编译通过
- [ ] 手动测试 NotebookEdit 密钥检测
- [ ] 现有单元测试全通过

## 后续推广

- [ ] 通知服务提取为 node
- [ ] 密钥检测提取为 node
- [ ] 备份服务提取为 node
- [ ] 全局工具处理器按 node/插件哲学改造
