# persistence — LSM树持久化

> 📍 **导航**: [modules/](../README.md) › persistence

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [KvStore](kv_store/KvStore.csproj) | PithosDB KV 存储适配层 — IKvStore 实现、租约与 DI 注册 |
| [PithosDB.Core](pithosdb/PithosDB.Core.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/persistence/module.slnx
dotnet pack modules/persistence/module.slnx -o ./nuget-feed
```
