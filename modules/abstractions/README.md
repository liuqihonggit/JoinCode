# abstractions — 抽象接口+基础设施+异步原语

> 📍 **导航**: [modules/](../README.md) › abstractions

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [Abstractions.Configuration](abstractions_configuration/Abstractions.Configuration.csproj) |  |
| [Abstractions.Entity](abstractions_entity/Abstractions.Entity.csproj) |  |
| [Abstractions.Interfaces](abstractions_interfaces/Abstractions.Interfaces.csproj) |  |
| [Abstractions.Models](abstractions_models/Abstractions.Models.csproj) |  |
| [Abstractions.Primitives.Attributes](abstractions_primitives_attributes/Abstractions.Primitives.Attributes.csproj) |  |
| [Abstractions.Primitives.Clock](abstractions_primitives_clock/Abstractions.Primitives.Clock.csproj) |  |
| [Abstractions.Primitives.Collections](abstractions_primitives_collections/Abstractions.Primitives.Collections.csproj) |  |
| [Abstractions.Primitives.Commands](abstractions_primitives_commands/Abstractions.Primitives.Commands.csproj) |  |
| [Abstractions.Primitives.Config](abstractions_primitives_config/Abstractions.Primitives.Config.csproj) |  |
| [Abstractions.Primitives.Configuration](abstractions_primitives_configuration/Abstractions.Primitives.Configuration.csproj) |  |
| [Abstractions.Primitives.Exceptions](abstractions_primitives_exceptions/Abstractions.Primitives.Exceptions.csproj) |  |
| [Abstractions.Primitives.Execution](abstractions_primitives_execution/Abstractions.Primitives.Execution.csproj) |  |
| [Abstractions.Primitives.Http](abstractions_primitives_http/Abstractions.Primitives.Http.csproj) |  |
| [Abstractions.Primitives.Interfaces](abstractions_primitives_interfaces/Abstractions.Primitives.Interfaces.csproj) |  |
| [Abstractions.Primitives.Localization](abstractions_primitives_localization/Abstractions.Primitives.Localization.csproj) |  |
| [Abstractions.Primitives.Models](abstractions_primitives_models/Abstractions.Primitives.Models.csproj) |  |
| [Abstractions.Primitives.Pipeline](abstractions_primitives_pipeline/Abstractions.Primitives.Pipeline.csproj) |  |
| [Abstractions.Primitives.Security](abstractions_primitives_security/Abstractions.Primitives.Security.csproj) |  |
| [Abstractions.Primitives.Shared](abstractions_primitives_shared/Abstractions.Primitives.Shared.csproj) |  |
| [Abstractions.Primitives.Shell](abstractions_primitives_shell/Abstractions.Primitives.Shell.csproj) |  |
| [Abstractions.Primitives.Tools.Tests](abstractions_primitives_tools_tests/Abstractions.Primitives.Tools.Tests.csproj) | 测试 |
| [Abstractions.Primitives.Tools](abstractions_primitives_tools/Abstractions.Primitives.Tools.csproj) |  |
| [Abstractions.Primitives.Utils](abstractions_primitives_utils/Abstractions.Primitives.Utils.csproj) |  |
| [Abstractions.Utils](abstractions_utils/Abstractions.Utils.csproj) |  |
| [Abstractions](abstractions/Abstractions.csproj) |  |
| [Abstractions.Agents](agents/Abstractions.Agents.csproj) |  |
| [Abstractions.AI](ai/Abstractions.AI.csproj) |  |
| [AsyncLock.Tests](async_lock_tests/AsyncLock.Tests.csproj) | 测试 |
| [AsyncLock](async_lock/AsyncLock.csproj) |  |
| [JoinCode.BclBridge.Tests](bcl_bridge_tests/JoinCode.BclBridge.Tests.csproj) | 测试 |
| [BclBridge](bcl_bridge/BclBridge.csproj) |  |
| [Abstractions.Brain](brain/Abstractions.Brain.csproj) |  |
| [Abstractions.Composition](composition/Abstractions.Composition.csproj) |  |
| [Abstractions.Exceptions](exceptions/Abstractions.Exceptions.csproj) |  |
| [Abstractions.Guard](guard/Abstractions.Guard.csproj) |  |
| [Abstractions.Hands](hands/Abstractions.Hands.csproj) |  |
| [Infrastructure.Configuration](infrastructure_configuration/Infrastructure.Configuration.csproj) |  |
| [Infrastructure.HotSpot](infrastructure_hot_spot/Infrastructure.HotSpot.csproj) |  |
| [Infrastructure.Http](infrastructure_http/Infrastructure.Http.csproj) |  |
| [Infrastructure.Localization](infrastructure_localization/Infrastructure.Localization.csproj) |  |
| [Infrastructure.Process](infrastructure_process/Infrastructure.Process.csproj) |  |
| [Infrastructure.Resilience](infrastructure_resilience/Infrastructure.Resilience.csproj) |  |
| [Infrastructure.Telemetry](infrastructure_telemetry/Infrastructure.Telemetry.csproj) |  |
| [Infrastructure](infrastructure/Infrastructure.csproj) |  |
| [Abstractions.Memory](memory/Abstractions.Memory.csproj) |  |
| [Abstractions.Perception](perception/Abstractions.Perception.csproj) |  |
| [Abstractions.Primitives](primitives/Abstractions.Primitives.csproj) |  |
| [Transport.Contracts](transport_contracts/Transport.Contracts.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/abstractions/module.slnx
dotnet pack modules/abstractions/module.slnx -o ./nuget-feed
```
