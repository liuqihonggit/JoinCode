# storage — 存储+调度+下载

> 📍 **导航**: [modules/](../README.md) › storage

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [Clock.Tests](clock_tests/Clock.Tests.csproj) | 测试 |
| [Clock](clock/Clock.csproj) |  |
| [Downloader.Tests](downloader_tests/Downloader.Tests.csproj) | 测试 |
| [Downloader](downloader/Downloader.csproj) |  |
| [Scheduling.Tests](scheduling_tests/Scheduling.Tests.csproj) | 测试 |
| [Scheduling](scheduling/Scheduling.csproj) |  |
| [Vault.Memdir.Tests](vault_memdir_tests/Vault.Memdir.Tests.csproj) | 测试 |
| [Vault.Other.Tests](vault_other_tests/Vault.Other.Tests.csproj) | 测试 |
| [Vault.Testing.Common](vault_testing_common/Vault.Testing.Common.csproj) |  |
| [Vault](vault/Vault.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/storage/module.slnx
dotnet pack modules/storage/module.slnx -o ./nuget-feed
```
