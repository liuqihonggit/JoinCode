# services — 服务层(桥接+浏览器+梦境+更新+沙箱)

> 📍 **导航**: [modules/](../README.md) › services

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [Bridge.Config](bridge_config/Bridge.Config.csproj) |  |
| [Bridge.Tests](bridge_tests/Bridge.Tests.csproj) | 测试 |
| [Bridge](bridge/Bridge.csproj) |  |
| [Browser.Tests](browser_tests/Browser.Tests.csproj) | 测试 |
| [Browser](browser/Browser.csproj) |  |
| [Dream.Tests](dream_tests/Dream.Tests.csproj) | 测试 |
| [Dream](dream/Dream.csproj) |  |
| [Hub.Tests](hub_tests/Hub.Tests.csproj) | 测试 |
| [Hub](hub/Hub.csproj) |  |
| [SandboxSatellite](sandbox_satellite/SandboxSatellite.csproj) |  |
| [Update](update/Update.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/services/module.slnx
dotnet pack modules/services/module.slnx -o ./nuget-feed
```
