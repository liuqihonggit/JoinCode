# 0057. TS 原版 P0 缺口补齐 — LSP 集成 + Analytics 分析

- 状态：proposed
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0056](0056-cache-break-detection-enhancement.md)（缓存破坏检测维度补齐）
- 背景：jcc vs Claude Code TS 原版全量差异分析

## 背景

对 jcc（C# 复刻版）与 Claude Code TS 原版（`claude-code-rev-main/src`）进行全量差异分析后，识别出 P0 高价值缺口。

### 原识别 3 个缺口，调整后 2 个

**YOLO 分类器（移除）**：TS `yoloClassifier.ts`（53KB）是 **ANT-ONLY 功能**（`process.env.USER_TYPE === 'ant'`），外部版本 `bashClassifier.ts` 是 stub（所有函数返回空/false）。jcc 的 `AutoModeClassifier`（181 行规则驱动）已比 TS 外部版本更完善。**不需要补齐**。

### 缺口1：LSP 集成（语言服务器协议）

TS 原版 `tools/LSPTool/` 实现了 LSP 集成：
- `LSPTool.ts` — LSP 工具主入口
- `symbolContext.ts` — 符号上下文提取（定义/引用/类型信息）
- `schemas.ts` — 请求/响应 schema
- `prompt.ts` — LSP 工具提示词
- **影响**：jcc 缺少代码智能补全/诊断/跳转定义/查找引用能力

### 缺口2：Analytics 分析框架

TS 原版 `services/analytics/` 实现了使用数据收集：
- `firstPartyEventLogger.ts` — 第一方事件日志
- `datadog.ts` — Datadog 集成
- `growthbook.ts` — GrowthBook A/B 测试
- `sink.ts` + `sinkKillswitch.ts` — 数据汇 + 熔断开关
- `metadata.ts` — 事件元数据
- **影响**：jcc 缺少使用度量/事件追踪/实验框架，无法量化功能使用率

## 决策（提议）

分 2 批渐进式补齐，每批独立可提交：

### 批1：LSP 集成

**目标**：实现 LSP 客户端，支持代码智能补全/诊断

**改动范围**：
1. `core/search/CodeIndex/src/` — 新增 `LspClient.cs`（LSP 客户端）
   - 启动/管理 LSP server 进程（如 `clangd`、`rust-analyzer`、`omnisharp`）
   - JSON-RPC 2.0 通信
   - 请求：textDocument/completion、textDocument/definition、textDocument/references、textDocument/diagnostic
2. `core/execution/Hands/src/` — 新增 LSP 工具 Handler
   - `[McpTool]` 标记 + 源码生成器自动注册
   - 工具：`lsp_complete`、`lsp_definition`、`lsp_references`、`lsp_diagnostic`

**AOT 约束**：
- JSON-RPC 用 `JsonContext` + 源码生成器
- LSP server 进程用 `Process` 管理，stdout/stderr 异步读取

### 批2：Analytics 分析框架

**目标**：实现使用数据收集框架

**改动范围**：
1. `infrastructure/Infrastructure/src/Analytics/` — 新增分析核心
   - `IEventLogger` 接口 + `FirstPartyEventLogger` 实现
   - `EventSink` — 事件汇（批量写入/定时 flush）
   - `EventMetadata` — 事件元数据（sessionId/timestamp/modelId/toolName）
   - `SinkKillswitch` — 熔断开关（配置关闭/采样率控制）
2. 全项目 `logEvent` 调用点 — 在关键路径埋点（工具调用/API 调用/缓存破坏/上下文压缩）

**AOT 约束**：
- 事件写入用 `FileShare.ReadWrite` + `FileMode.Append`
- 批量写入用 `Channel<T>` 异步队列
- 禁止 `Datadog`/`GrowthBook` 第三方 SDK（不兼容 NativeAOT），用自实现替代

## 替代方案

### 方案 A：只补 LSP，不补 Analytics（放弃）

- **放弃原因**：Analytics 是度量基础（无法量化功能使用率），LSP 是开发者核心需求，两者互补
- **适用场景**：若用户只想快速提升开发体验，可只做批1

### 方案 B：用第三方库替代自实现（放弃）

- LSP 用 `OmniSharp` / `clangd` 现成客户端
- Analytics 用 `Application Insights` / `OpenTelemetry`
- **放弃原因**：大部分第三方库不兼容 NativeAOT（反射 emit/dynamic），违反项目约束
- **例外**：`OpenTelemetry` SDK 有 AOT 兼容模式，可考虑后续接入

## 后果

- **正面**：
  - 批1 LSP：代码智能补全/诊断/跳转定义/查找引用，开发者核心生产力
  - 批2 Analytics：使用度量/事件追踪，量化功能使用率指导优化方向
- **负面**：
  - 批1 LSP：LSP server 进程管理复杂（启动/重启/多语言支持），内存占用增加
  - 批2 Analytics：事件写入 IO 开销（可通过采样率+异步队列缓解）
- **中性**：
  - 两批改动独立，可分 2 个 PR 合并

## 实现顺序

```
批1 LSP 集成 → 批2 Analytics 分析
```

每批遵循：ADR → 红测试 → 实现 → 编译 → 绿测试 → git 提交

## 验证标准

| 批 | 验证标准 |
|----|----------|
| 批1 LSP | `lsp_complete` 工具返回补全列表，`lsp_definition` 跳转定义 |
| 批2 Analytics | `logEvent` 事件写入 `.jcc/analytics/` 目录，含 sessionId/timestamp/toolName |
