# apps — 应用入口(CLI+GUI+TUI+SDK)

> 📍 **导航**: [modules/](../README.md) › apps

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [JoinCode](cli/JoinCode.csproj) |  |
| [JoinCode.Engine](engine/JoinCode.Engine.csproj) | 应用入口 |
| [Sdk](sdk/Sdk.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/apps/module.slnx
dotnet pack modules/apps/module.slnx -o ./nuget-feed
```
