# collections — 纯数据结构功能域

> 📍 **导航**: [modules/](../README.md) › collections

## 功能

纯数据结构库，零外部依赖。提供 HAMT（Hash Array Mapped Trie）、ImmutableList 等持久化数据结构。

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [Structura.Tests](structura_tests/Structura.Tests.csproj) | 测试 |
| [Structura](structura/Structura.csproj) |  |
<!-- END MODULE PROJECTS -->

## 依赖

无（零依赖底层库）。

## 被依赖

- abstractions、async_lock、infrastructure、code_index、structura.benchmarks

## 用法

```bash
# 独立编译
dotnet build modules/collections/module.slnx

# 打包
dotnet pack modules/collections/module.slnx -o ./nuget-feed
```
