# 0108. Dispose 一致性分析器规则与 OnDispose 间接层消除

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-09-16
- 决策者：用户（liuqihonggit）+ AI

## 背景

项目代码库中存在三类资源释放不一致问题：

1. **try-finally + 手动 Dispose**：创建 IDisposable 对象后用 try-finally 释放，而非 using var。样板冗余、易遗漏、无法保证异常安全。
2. **Dispose 内 try-catch 样板**：Dispose 方法里用多个独立 try-catch 包裹每个资源的 Cancel/Dispose，吞 ObjectDisposedException。典型反例：3 个 try-catch 各包裹一个资源释放。
3. **IAsyncDisposable 同步 Dispose**：对仅实现 IAsyncDisposable 的类型调用同步 Dispose()，跳过异步清理逻辑，导致句柄泄露/数据丢失。

此外，Entity 基类使用 OnDispose() 模板方法模式（`Dispose()` 调 `protected abstract void OnDispose()`），102 个子类 override OnDispose()。这导致：
- 释放方法名不统一（OnDispose / Close / Stop / Shutdown / Dispose / DisposeAsync 六种变体）
- 分析器无法识别 OnDispose 等自定义变体作为释放方法
- 间接层增加理解成本，无实际价值

用户明确要求：
- 不允许任何豁免注释（如 `// leave-open`），分析器必须通过 AST 语义分析精确识别
- 强制条例：Error 级别，编译期拦截，不允许破例
- 统一命名：只认 Dispose() 和 DisposeAsync() 两种

## 决策

### 决策1：新增三条分析器规则（Error 级别，编译期拦截）

| 规则 | 检测 | 正确做法 |
|------|------|----------|
| JCC9105 | try-finally 中手动 Dispose | 改用 `using var` / `await using var` |
| JCC9106 | Dispose 方法内 try-catch 样板 | 改用 `DisposeSafe` / `CancelAndDisposeSafe` 扩展方法 |
| JCC9107 | IAsyncDisposable 对象同步 Dispose | 改用 `await DisposeAsync()` 或 `await using var` |

三条规则均为 `DiagnosticSeverity.Error`，编译期拦截，无豁免。AST 语义分析自动识别可转换场景，排除有 catch/return/foreach/字段接收/非 new 对象初始化等不可转换情况。

### 决策2：删除 OnDispose 间接层，统一为 Dispose/DisposeAsync

- Entity 基类：去掉 `protected abstract void OnDispose()`，改为 `public virtual void Dispose()` 和 `public virtual ValueTask DisposeAsync()`
- 102 个子类：`override void OnDispose()` → `override void Dispose()` + `base.Dispose()`
- 分析器 `IsInsideDisposeMethod` 只检测 `"Dispose" or "DisposeAsync"`，不检测 OnDispose/Close/Stop/Shutdown 等变体

### 决策3：McpOAuthService 收拢为 DisposeAsync

McpOAuthService 持有 `McpPkceAuthProvider`（仅实现 IAsyncDisposable），原 Dispose() 中同步调用 `_authProvider.Dispose()` 是 JCC9107 违反。改为 `override async ValueTask DisposeAsync()`，用 `await _authProvider.DisposeAsync()` 异步释放。

## 替代方案

### 方案A：保留 OnDispose + 扩展分析器识别 OnDispose

- 优点：改动小，102 个子类不用改
- 缺点：分析器需识别 OnDispose/Close/Stop/Shutdown 等所有变体，复杂度高且易遗漏；间接层无实际价值；命名不统一增加理解成本
- **放弃原因**：用户明确要求统一命名，且分析器识别变体不如统一命名可靠

### 方案B：Warning 级别 + 逐步修复

- 优点：渐进式，不阻塞编译
- 缺点：Warning 会被忽略，无法保证及时修复；TreatWarningsAsErrors=true 已启用，Warning 等效于 Error
- **放弃原因**：项目已启用 TreatWarningsAsErrors，Warning 和 Error 效果相同；用户要求强制条例不允许破例

### 方案C：运行时检查（Roslyn 分析器之外的运行时检测）

- 优点：可检测动态加载的类型
- 缺点：运行时才发现问题，无法在编译期拦截；性能开销；无法覆盖所有场景
- **放弃原因**：编译期拦截优于运行时检测，早发现早修复

## 验证

- 全量编译：0 警告 0 错误（`dotnet build build/sln/JoinCode.slnx --no-incremental`）
- 分析器测试：17 个全绿（`dotnet test gen/aot_safety.tests`）
- Guard.Config.Tests：1119 个全绿
- Infra.Services.Tests：490 个全绿
- Infra.Utils.Tests：370 个通过，4 个预存在失败（2 个 AsyncLock 偶发 + 2 个 ToolExecutionEntityRegistry 逻辑问题，非本次回归）
- git 提交：`5d4508eb2`（106 files changed, 598 insertions, 239 deletions）

## 影响范围

- `gen/aot_safety.generator/DisposableConsistencyRules.cs` — 三条分析器规则
- `gen/aot_safety.tests/DisposableConsistencyRulesTests.cs` — 17 个测试用例
- `lib/abstractions/abs_core/core_entity/Entity.cs` — 基类改造
- 102 个子类 — OnDispose → Dispose + base.Dispose()
- `kit/mcp/auth/o_auth/McpOAuthService.cs` — 收拢为 DisposeAsync
- AGENTS.md 规则1-4 已有对应规范，本 ADR 为编译期强制手段

## 关联

- 上游：[ADR 0093](0093-resource-management-exception-style.md) — 资源管理与异常控制风格规范（运行时规范）
- 本 ADR：编译期强制手段（分析器规则），补充 ADR 0093 的运行时规范
