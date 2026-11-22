# TASK118 — 通用设置迁移框架（TASK103 §2.3 #49a 取证）

> 状态：**框架已实现 / 关闭（账实不符）** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#49a / TASK071-A4**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「`SettingsSourceMigration` 只落地一件，通用框架未建（TASK089）」**已过时**：通用框架在提交 `a01b9e4a6`（2026-11-18，TASK089-092）已落地并接进生产读取路径，总账未回填。

- `ConfigMigrationRunner`（`modules/guard/guard.configuration/core/loading/migrations/ConfigMigrationRunner.cs:16-100`）：按 `IConfigMigration.Version` 升序执行、每步成功即落 `~/.jcc/config-migrations.json`（`:61/91-99`）、失败停在该版本不推进（`:78-87`）、并发最坏重复执行一次所以要求每条迁移幂等（`:13-14`）。显式登记而非反射（`:17-25`，AOT 友好且顺序可审）。
- `IConfigMigration`（同目录 `:4-16`）定义唯一递增版本 + `ApplyAsync`；已有一步 `SettingsSourceMigration`（v1，搬 settings.json 的 global 键）。
- 生产消费：`ConfigurationService.RunMigrationOnceAsync`（`modules/guard/guard/configuration/services/ConfigurationService.cs:75-86`）在每次读配置前经 `EnsureMigratedAsync` 调 `ConfigMigrationRunner.RunAsync`，并用 `MarkInternalWrite` 避免 watcher 回灌——不是"只有类没人用"。
- 测试：`guard.config.tests/configuration/core/ConfigMigrationRunnerTests.cs`，本轮筛选 `ConfigMigration` **8/8 绿**（版本推进、失败停驻、幂等/历史上限等）。

与基准的差别只是**迁移的载体**：基准是 11 个 JS 脚本目录（运行时脚本化），本仓是编译期显式登记的 C# 迁移。功能等价（版本号 + 一次性 + 失败可重试 + 幂等），且更适合本仓 AOT 与可审计要求；不需要再建一套脚本目录（会造成双源）。

---

## 1. 四问回答

### 问1 专家隐性实践
配置迁移框架的必要性不在"搬一次键"，而在**非幂等变更**（改值/合并/换格式）需要"跑没跑过"的可靠标记。成熟做法是显式版本 + 每步落盘 + 失败停驻 + 迁移本身幂等；脚本化或编译化只是载体差异。

### 问2 原始素材（一手）
- 本仓：`ConfigMigrationRunner.cs:4-14/16-100`、`IConfigMigration.cs:4-16/43-58`、`SettingsSourceMigration.cs:10-16`、`ConfigurationService.cs:75-86`、`AppDataPaths.cs:112-115`、`ConfigMigrationRunnerTests.cs`。
- 基准：TASK071 原文提到的 11 个迁移脚本（TS 迁移框架）——本轮不重复搬运，因为本仓四要素（版本/标记/停驻/幂等）都已具备。

### 问3 具体方法与工具
从"框架存在？→ 有生产消费？→ 有测试？"三问逐项查证：`grep ConfigMigrationRunner` 同时命中定义、ConfigurationService 调用与测试；再跑筛选测试验证当前实现仍绿。

### 问4 独立验证来源
`AppDataPaths.ConfigMigrationsFilePath`（`:115`）独立定义标记文件位置，与 Runner 的读写路径一致；`ConfigMigrationReport.FromVersion/ToVersion/FailedVersion`（`IConfigMigration.cs:75-87`）独立印证版本推进语义。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| 迁移载体 | 11 个 JS 脚本目录 | 编译期显式登记 C# 迁移 | 实现差异，不弱 |
| 版本号 | 脚本版本 | `IConfigMigration.Version` + marker | 已对齐 |
| 一次性/失败重试 | 脚本框架负责 | Runner 每步落盘、失败停驻 | 已对齐 |
| 并发 | 未核实 | 最坏重复执行，要求迁移幂等 | 本仓显式约束 |
| 生产消费 | 启动/读取时跑 | `ConfigurationService.EnsureMigratedAsync` | 已对齐 |

**不建脚本目录的理由**：新增第二套迁移机制会让"哪个版本是真的"产生二义性（与 #36 的 settings/capabilities 双源同理）；已有框架加一步只需在 `Migrations` 列表加一行 + 一个 `IConfigMigration` 实现。

---

## 3. 验收（取证项，无代码改动）

- `Guard.Config.Tests` 筛选 `ConfigMigration` **8/8 绿**。
- 总账 #49a 划掉并指向本文档。
