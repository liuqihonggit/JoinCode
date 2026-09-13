# 全局推广 node/插件 — 从 WriteDefenseService 提取独立 node

> ADR: [0104](../adr/0104-write-defense-extract-public-node.md)（WriteDefenseService 公共对象提取）

## 目标

将 WriteDefenseService 内 11 个防御步骤按依赖簇拆分为独立 node/插件对象，每个 node 可被任意工具注入使用，WriteDefenseService 退化为薄编排层。

## 架构

```
defense/
├── WriteDefense.cs           ← 链式构建器（已有，不动）
├── WriteDefenseService.cs    ← 薄编排层，注入各 node，适配 ToolResult
├── SecretGuardNode.cs        ← 密钥检测（ITeamMemSecretGuard）
├── FileBackupNode.cs         ← 备份服务（IFileHistoryService + IFileSystem）
├── WriteNotifyNode.cs        ← 通知服务（LSP + 遥测 + 监听器）
├── SandboxGuardNode.cs       ← 沙箱解析（ISandboxManager）
├── FileStateGuardNode.cs     ← 读前校验+脏写保护（IFileStateCache + IFileSystem）
├── PathGuardNode.cs          ← 纯路径守卫（无依赖，纯函数）
└── FormatValidatorNode.cs    ← Settings/Keyword/Doctor 校验
```

## node 设计原则

1. **node 返回泛型结果**（string? error），不耦合 ToolResult — 任意工具可用
2. **WriteDefenseService 适配** — 调用 node，将 error 包装为 ToolResult + 诊断
3. **node 用 [Register] 注册** — DI 自动注入，消费者各取所需
4. **node 独立可测** — 不依赖 WriteDefenseService，可单独 mock/测试

## 提取顺序（按复用价值排序）

| 序号 | Node | 依赖 | 复用场景 |
|------|------|------|----------|
| 1 | SecretGuardNode | ITeamMemSecretGuard | 文件写入、命令执行、任何处理用户内容的工具 |
| 2 | FileBackupNode | IFileHistoryService + IFileSystem | 文件写入、文件删除、任何修改文件的工具 |
| 3 | WriteNotifyNode | LSP + 遥测 + 监听器 | 文件写入、任何修改文件的工具 |
| 4 | SandboxGuardNode | ISandboxManager | 任何解析路径的工具 |
| 5 | FileStateGuardNode | IFileStateCache + IFileSystem | 文件写入/编辑 |
| 6 | PathGuardNode | 无 | 任何检查路径的工具 |
| 7 | FormatValidatorNode | IFileSystem + ISubAgentContextAccessor | 文件编辑 |

## 进度

- [ ] 1. SecretGuardNode
- [ ] 2. FileBackupNode
- [ ] 3. WriteNotifyNode
- [ ] 4. SandboxGuardNode
- [ ] 5. FileStateGuardNode
- [ ] 6. PathGuardNode
- [ ] 7. FormatValidatorNode
- [ ] 8. WriteDefenseService 改为薄编排层
- [ ] 9. 编译 + 测试 + 手动测试

<!-- 🤖 Auto Decision: 2026-09-13 -->
<!-- 决策: node 返回泛型结果 string?，不耦合 ToolResult -->
<!-- 原因: 任意工具可注入 node 而不需要知道 ToolResult/ToolDiagnostic，WriteDefenseService 负责适配 -->
<!-- 替代方案: node 直接返回 ToolResult（耦合高，不利于非工具消费者）-->
