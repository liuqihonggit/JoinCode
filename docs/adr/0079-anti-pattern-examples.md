# 0079. 反例清单（踩过的坑，禁止再犯）

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

团队在开发过程中踩过一系列坑，这些反例已部分收编为 ADR（详见 [docs/adr/README.md](README.md) 索引）。本文档汇总所有反例，禁止再犯。

## 详细内容

> 📖 部分反例已收编为 ADR，详见 [docs/adr/README.md](README.md) 索引。

### 反例1：不优先查阅 AGENTS.md 已有文档

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 自己摸索命令行参数格式 | 先查 AGENTS.md 的"CLI 运行时测试"和"踩坑记录"章节 |
| 用 ProcessStartInfo 手动拼接参数 | 用 AGENTS.md 文档化的 `Start-Process -ArgumentList "--port","9901"` 方式 |
| 修改共享配置文件（mockserver.json）来适配测试 | 用 `--port` 覆盖端口，`--config` 指定配置，不改文件 |
| 遇到问题自己猜方案 | 先查 AGENTS.md 踩坑记录，再查项目代码，最后才自己试 |

**根因**：AGENTS.md 是团队积累的操作手册，包含大量踩坑记录和验证过的命令。跳过它直接试错，浪费时间且容易引入新问题（如改了共享配置文件忘记恢复）。

### 反例2：修改共享配置文件来跑测试

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 改 `mockserver.json` 的端口/内容来适配 E2E | 用 `--port 9901` 覆盖端口 |
| 改 `mockserver_cluster.json` 的端口来匹配启动参数 | 启动时用 `--port` 覆盖，配置文件保持原始值 |
| 改完配置文件忘记恢复 | 不改配置文件，用命令行参数覆盖 |

**根因**：配置文件是项目共享的，改了会影响其他人。命令行参数覆盖是零副作用的。

### 反例3：治标不治本的修复链

> ADR: [0024](0024-no-symptomatic-fix-chain.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| FileShare.None 失败 → 加 FileShare 降级策略 | 先分析根因：是读-写冲突还是写-写冲突？ |
| 降级策略失败 → 换 AppendAllTextAsync | 识别跨进程 vs 同进程，选择正确的同步原语 |
| AppendAllTextAsync 失败 → 加重试 | 跨进程并发 = Named Mutex；读-写冲突 = FileShare.ReadWrite |
| 重试仍失败 → 继续换方案 | 停下来做方案，让用户确认方向 |

### 反例4：加法思维而非减法思维

> ADR: [0023](0023-subtraction-over-addition.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 加 `[DoNotAutoRegister]` 新特性来阻止 DI 注册 | 减少不必要的 DI 暴露（如 ShellProviderBase 不需要 IShellProvider） |
| 加 ShellCapabilityProvider DI 单例只为首次检测缓存 | 用静态 ShellCapabilityCache，启动时检测一次 |
| 加 FileShare 降级策略层 | 用 FileShare.ReadWrite + Named Mutex 一步到位 |

### 反例5：依赖模型 ID 字符串推断模态而非显式注册（配置大于代码）

> ADR: [0004](0004-config-over-code-modalities.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 代码里硬编码模型 ID 字符串模式推断模态（如含 `vision`→识图） | `settings.json` 的 `vendor.{provider}.models` 显式注册模型描述（含 `Capabilities.Modalities`） |
| `JCC_MODEL_ID` 指定未注册模型时静默推断补注册 | 无条件抛 `ConfigurationException[GRD016]`，要求用户先在 settings.json 注册 |
| `AutoFetchModels` 远程拉取新模型时从 ID 推断模态 | 远程新模型模态留默认（`Text`），用户在 settings.json 手动配置需要的模态 |

**定位文件**：`ConfigLoader.cs:582 EnsureEnvModelInConfig`、`ModelListMerger.cs:39 Merge`

### 反例6：序列化在读锁内完成时多余的 ToList 防御性拷贝

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 序列化已在 `ReaderWriterLockSlim` 读锁内完成，仍 `ToList()` 拷贝集合 | 直接用引用，读锁内无并发修改，序列化安全 |
| `CallEdges = _store.CallEdges.ToList()` + 锁内序列化 | `CallEdges = _store.CallEdges` + 锁内序列化 |

**根因**：`ToList()` 会分配新 List + 拷贝全部元素。如果序列化在锁外，需要拷贝防并发修改；但如果序列化已移入锁内（`using (var scope = _store.EnterReadLock()) { ... json = Serialize(data); }`），读锁阻止写锁获取，枚举集合安全，`ToList` 变为多余开销。

**判断方法**：看 `Serialize` 调用是否在 `using (scope = EnterReadLock())` 块内 — 是则去掉 ToList，否则保留。

**定位文件**：`GraphPersistence.cs:SaveAsync`

## 替代方案

无。这些反例均为团队实战踩坑，必须引以为戒。
