# 0081. 七层解决方案架构与编译策略

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

项目采用七层 slnx 隔离架构，必须按顺序编译，上层依赖下层的构建产物。本文档定义编译顺序、CI/开发编译策略与编译注意事项。

## 详细内容

### 七层解决方案架构（强制编译顺序）

项目采用七层 slnx 隔离架构，**必须按顺序编译**，上层依赖下层的构建产物：

| 编译顺序 | 解决方案 | 职责 | 目录 | 关键内容 |
|----------|----------|------|------|----------|
| ① | `Generators.slnx` | 源码生成器 | `generators/` | 9 个 Generator + 测试 |
| ② | `Foundation.slnx` | 基础抽象 | `foundation/` | Abstractions + Structura + Transport.Contracts |
| ③ | `Infrastructure.slnx` | 基础设施 | `infrastructure/` | Infrastructure + Transport.Impl |
| ④ | `Core.slnx` | 核心组件 | `core/` | ai/(Llm,Agents,Reasoning) + execution/(Brain,Hands,Scheduling,McpToolDispatch) + safety/(Guard,Vault) + search/(CodeIndex,Browser) |
| ⑤ | `Services.slnx` | 服务组件 | `services/` | Mcp + Dream + Eyes + Bridge |
| ⑥ | `Composition.slnx` | 组合层 | `composition/` | Composition + Clock |
| ⑦ | `App.slnx` | 主工程 | `app/` | JoinCode.exe + Sdk + 集成测试 + MockServers |

**依赖链**：`Generators` → `Foundation` → `Infrastructure` → `Core` → `Services` → `Composition` → `App`

**为什么必须按顺序？**
- `Generators.slnx` 包含源码生成器（EnumMetadata.Generator、McpToolDispatch.Generator 等），它们生成 `XxxConstants` 静态类
- `Foundation.slnx` 中的 Abstractions 需要生成器才能编译出枚举常量
- 如果跳层编译，依赖的 DLL 不存在，编译会失败

**CI 编译命令（Release + 全量）**：
```powershell
dotnet build Generators.slnx -c Release --no-incremental
dotnet build Foundation.slnx -c Release --no-incremental
dotnet build Infrastructure.slnx -c Release --no-incremental
dotnet build Core.slnx -c Release --no-incremental
dotnet build Services.slnx -c Release --no-incremental
dotnet build Composition.slnx -c Release --no-incremental
dotnet build App.slnx -c Release --no-incremental
```

**修改不同层时的编译策略**：
| 修改内容 | 需要重新编译的层 |
|----------|------------------|
| 枚举/Abstractions/generators | ①②③④⑤⑥⑦ 全部 |
| Infrastructure/Transport | ③④⑤⑥⑦ |
| 核心组件（core/） | ④⑤⑥⑦ |
| 服务组件（services/） | ⑤⑥⑦ |
| 组合层（composition/） | ⑥⑦ |
| 主工程源码（app/） | ⑦ |
| 仅测试代码 | 对应的 slnx |

### 开发编译策略（Debug + 增量 + 单 csproj）

**核心原则**：编码期间用 Debug 模式增量编译单个 csproj，Release 全量编译交给 CI。

**开发阶段（改代码时）**：
1. **只编译改动的那个 `.csproj`** — 例如改了 `Llm.csproj` 就只编译 `dotnet build core/ai/Llm/src/Llm.csproj -c Debug`，不编译整个 slnx
2. **使用 Debug 模式** — Debug 编译更快，无需 AOT/Trim 等优化开销
3. **连续修改多个文件时，改完所有文件后再编译一次** — 禁止改一个文件就编译
4. **只有影响面很大时才编译 slnx** — 例如改了 Abstractions 接口导致大量项目受影响，才用 `dotnet build Foundation.slnx -c Debug`

**提交前（git commit 前）**：
1. 不需要本地 Release 全量编译 — CI 会做
2. 只需确保改动的 csproj 在 Debug 模式下编译通过即可提交

**开发编译命令示例**：
```powershell
# 改了 Llm 组件 → 只编译那个 csproj
dotnet build core/ai/Llm/src/Llm.csproj -c Debug
# 改了主工程 CliSession → 只编译主工程
dotnet build app/JoinCode/JoinCode.csproj -c Debug
# 改了 Abstractions → 影响面大，编译基础层 slnx
dotnet build Foundation.slnx -c Debug
```

**CI 全量编译命令（Release + --no-incremental）**：
```powershell
dotnet build Generators.slnx -c Release --no-incremental; dotnet build Foundation.slnx -c Release --no-incremental; dotnet build Infrastructure.slnx -c Release --no-incremental; dotnet build Core.slnx -c Release --no-incremental; dotnet build Services.slnx -c Release --no-incremental; dotnet build Composition.slnx -c Release --no-incremental; dotnet build App.slnx -c Release --no-incremental
```

### 编译注意事项

1. 当遇到编译锁定,编译时候打不开,编译不了,表示有`其他CLI项目`编译中,当前电脑内存紧迫,你只能用 wait 30s 之后再尝试执行编译.
2. 你有 wait 工具吗? 没有的话尝试 powershell 里面的.
3. 一直尝试就好,不要放弃,你肯定可以某个时机交错编译得出来的.

## 替代方案

无。七层隔离架构是项目既定设计，保证依赖方向清晰、编译可增量。
