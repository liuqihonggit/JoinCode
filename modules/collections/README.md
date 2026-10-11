# collections — 纯数据结构功能域

> 📍 **导航**: [modules/](../README.md) › collections

## 功能

纯数据结构库，零外部依赖。提供 HAMT（Hash Array Mapped Trie）、ImmutableList 等持久化数据结构。

## 项目

| 项目 | 说明 |
|------|------|
| structura | 持久化数据结构（HAMT、ImmutableList 等） |
| structura.tests | 单元测试 |

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
