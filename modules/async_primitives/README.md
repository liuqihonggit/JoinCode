# async_primitives — 异步原语+传输层

> 📍 **导航**: [modules/](../README.md) › async_primitives

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [AsyncLock.Tests](async_lock.tests/AsyncLock.Tests.csproj) | 测试 |
| [AsyncLock](async_lock/AsyncLock.csproj) |  |
| [JoinCode.BclBridge.Tests](bcl_bridge.tests/JoinCode.BclBridge.Tests.csproj) | 测试 |
| [BclBridge](bcl_bridge/BclBridge.csproj) |  |
| [Transport.Contracts](transport.contracts/Transport.Contracts.csproj) |  |
| [Transport.Impl.Tests](transport.impl.tests/Transport.Impl.Tests.csproj) | 测试 |
| [Transport.Impl](transport.impl/Transport.Impl.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/async_primitives/module.slnx
dotnet pack modules/async_primitives/module.slnx -o ./nuget-feed
```
