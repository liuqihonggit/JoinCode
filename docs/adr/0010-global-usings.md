# 0010. GlobalUsings 统一管理

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

C# 项目中每个 `.cs` 文件顶部常有一堆 `using` 语句，重复且分散。新增文件需手动加 using，易遗漏；文件头部长度膨胀，影响阅读。

## 决策

1. **`.cs` 文件内禁止写 `using` 语句**，统一放 `GlobalUsings.cs`
2. **`Directory.Build.props` 全局 `using System.Linq`**：所有源码项目自动引用
3. **每个项目维护自己的 `GlobalUsings.cs`**，集中管理项目级 using

## 替代方案

1. **每个文件自己写 using**：放弃。重复、分散、易遗漏。
2. **只用 `Directory.Build.props` 全局 using**：放弃。项目特有命名空间不适合全局，需项目级 `GlobalUsings.cs` 补充。
3. **用 `ImplicitUsings` 属性**：部分采用。`ImplicitUsings` 启用标准隐式 using，但项目特有命名空间仍需 `GlobalUsings.cs`。

## 后果

- 正面：文件头部干净；新增文件无需写 using；using 集中管理易审计
- 负面：删除某命名空间需在 `GlobalUsings.cs` 删除该 `global using` 行，否则编译警告
- 中性：归档文件时需同步清理 `GlobalUsings.cs` 中对应旧命名空间（见 ADR 0008）
