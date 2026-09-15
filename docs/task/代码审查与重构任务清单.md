# 代码审查与重构任务清单

> 起始:2026-09-15 | 基于 5 个并行子代理审查报告 `docs/reference/code_review_report_20260915.md`

## 已完成 ✅

| # | commit | 内容 |
|---|--------|------|
| 1 | `5e0c58d1a` | 归档 10 个陈旧文件到 .xxx/ + 修复 AGENTS.md/ADR/docs 路径引用 |
| 2 | `f5fe591d8` | BridgeSubprocessManager 3 处 try-catch 改用 DisposeSafe |
| 3 | `bdc7cffd1` | BridgeMain 提取 2 个有名方法消除重复 lambda |
| 4 | `67e81feb3` | ServiceHost 旧式空检查改用 ThrowIfNull |
| 5 | `66851e297` | 25 处内联遥测字典统一为 ToolTelemetryHelper.RecordToolCount |
| 6 | `2583965c3` | 枚举补全第一批:13 个枚举 + EnumValueAttribute 迁移到 async_lock |
| 7 | `bfd82eef0` | 枚举补全第二批:28 个枚举 |
| 8 | `43a1f06a8` | 枚举补全第三批:52 个枚举 |
| 9 | `7e8f5378e` | 生成器 Constants 类改名消除 CS0101 冲突(442 文件) |
| 10 | `d54deffe5` | 枚举补全第四批:58 个枚举(覆盖剩余全部) |

## 已放弃 ❌

| 内容 | 原因 |
|------|------|
| Core.Utils → JoinCode.Core.Utils 命名空间统一 | Core 是巨型根命名空间(1265 声明),单独改会全部断裂 |
| 58 个枚举补全 [EnumValue](第四批) | 用户决定暂停,先 PR |

## 待办 ⏳

| # | 内容 | 状态 |
|---|------|------|
| 1 | ~~创建 PR(w3 → main,auto-merge squash)~~ | ✅ [#240](https://github.com/liuqihonggit/JoinCode/pull/240) |
| 2 | ~~58 个枚举补全 [EnumValue]~~ | ✅ `d54deffe5` |

## 关键决策记录

### 生成器改名方案(ADR 级别)
- **问题**:BridgeCliArg/DreamCliArg 等枚举已有 [CliOption],CliOptionGenerator 生成 `XxxConstants`。加 [EnumValue] 后,EnumMetadataGenerator 也生成同名 `XxxConstants` → CS0101
- **决策**:两个生成器各管各命名 — `XxxEnumConstants`(EnumMetadataGenerator) + `XxxCliOptionConstants`(CliOptionGenerator)
- **替代方案**:不给 6 个有 [CliOption] 的枚举加 [EnumValue](放弃,因为改名方案更彻底)
- **影响**:442 个文件同步替换引用

### EnumValueAttribute 位置迁移
- **从**:`lib/abstractions/` → **到**:`lib/async_lock/`(保持 `JoinCode.Abstractions.Attributes` 命名空间)
- **原因**:Abstractions.csproj 引用 AsyncLock.csproj(非反向),避免循环依赖

<!-- 🤖 Auto Decision: 2026-09-15 -->
<!-- 决策: 生成器改名而非跳过 CliOption 枚举 -->
<!-- 原因: 改名方案更彻底,两个生成器各管各命名空间,不冲突 -->
<!-- 替代方案: 不给 6 个 CliOption 枚举加 [EnumValue](用户最终选择改名) -->
<!-- 验证: 编译通过,442 文件同步替换 ✅ -->
