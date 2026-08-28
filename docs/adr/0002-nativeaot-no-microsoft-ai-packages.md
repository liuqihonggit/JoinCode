# 0002. NativeAOT + 禁用微软 AI 包

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目目标为发布单文件原生可执行 CLI（`jcc.exe`），要求启动快、内存占用低、无运行时依赖。同时项目大量使用 LLM/AI 能力，微软官方提供了 `Microsoft.Extensions.AI` 等 AI 包。

## 决策

1. **强制 NativeAOT**：Release 模式自动启用 `PublishAot` + `TrimMode=full`
2. **拒绝全部微软 AI 包**：大部分不支持 NativeAOT
3. **AOT 兼容约束**：禁止 `dynamic`、反射 emit、直接解析 JSON；必须用 `JsonContext` + 源码生成器
4. **所有源码项目标记 `IsAotCompatible`**

## 替代方案

1. **用 `Microsoft.Extensions.AI` 抽象**：放弃。该包依赖 `dynamic` 和反射，与 NativeAOT 不兼容，trimmer 会警告并最终运行时失败。
2. **用 JIT 发布（非 AOT）**：放弃。启动慢、需携带运行时、分发体积大，不符合 CLI 单文件目标。
3. **自研 AI 抽象层**：采用。在 `foundation/Abstractions` 中定义自己的 `ILlmClient`、`IToolHandler` 等抽象，AOT 友好。

## 后果

- 正面：单文件发布、启动毫秒级、内存占用低、无运行时依赖
- 负面：不能用微软 AI 生态的现成包，所有 AI 抽象需自研；JSON 必须用源码生成器 `JsonContext`，新增类型需手动注册
- 中性：`TreatWarningsAsErrors` + AOT 分析器形成零警告容忍，编译期捕获 AOT 不兼容代码
