# 0001. 七层 slnx 隔离架构

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目包含源码生成器、基础抽象、基础设施、核心组件、服务组件、组合层、主工程等不同性质的模块，总计上百个 csproj。若用单个 `.sln`，会出现：

1. 编译顺序不可控，上层依赖下层的构建产物时可能因跳层编译而失败
2. 源码生成器（Generators）必须先编译出 DLL，Foundation 才能引用生成的 `XxxConstants`
3. IDE 全量加载所有项目，内存占用高、导航缓慢
4. CI 无法分层并行

## 决策

采用七层 `.slnx` 隔离架构，按依赖链严格分层：

| 顺序 | 解决方案 | 职责 |
|------|----------|------|
| ① | `Generators.slnx` | 源码生成器 |
| ② | `Foundation.slnx` | 基础抽象 |
| ③ | `Infrastructure.slnx` | 基础设施 |
| ④ | `Core.slnx` | 核心组件 |
| ⑤ | `Services.slnx` | 服务组件 |
| ⑥ | `Composition.slnx` | 组合层 |
| ⑦ | `App.slnx` | 主工程 |

依赖链：`Generators → Foundation → Infrastructure → Core → Services → Composition → App`，必须按顺序编译。

## 替代方案

1. **单个 .sln 全量解决方案**：放弃。编译顺序依赖 MSBuild 自动推断，但源码生成器的增量缓存会导致新 `[Register]` 类型不被重新扫描，需手动 `--no-incremental`，单 sln 无法表达这种分层强制。
2. **按目录自动分层的 sln**：放弃。目录结构变化会破坏分层，缺乏显式约束。
3. **.slnx + Directory.Build.props 全局引用**：部分采用。`Directory.Build.props` 提供全局 `using System.Linq`，但解决方案分层仍用 slnx 显式表达。

## 后果

- 正面：编译顺序显式可控；IDE 可只打开当前工作层；CI 可分层触发；依赖方向清晰
- 负面：新增项目需手动加入对应 slnx；跨层重构需编译多层验证
- 中性：开发期用单 csproj Debug 增量编译，CI 用全量 Release 编译，两套策略并存
