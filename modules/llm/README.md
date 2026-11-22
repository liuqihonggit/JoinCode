# llm — LLM引擎+Agent+推理

> 📍 **导航**: [modules/](../README.md) › llm

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [Agents.Configuration](agents_configuration/Agents.Configuration.csproj) |  |
| [Agents.Coordinator.Backend](agents_coordinator_backend/Agents.Coordinator.Backend.csproj) |  |
| [Agents.Coordinator.Core](agents_coordinator_core/Agents.Coordinator.Core.csproj) |  |
| [Agents.Core](agents_core/Agents.Core.csproj) |  |
| [Agents.Discovery](agents_discovery/Agents.Discovery.csproj) |  |
| [Agents.DualModel](agents_dual_model/Agents.DualModel.csproj) |  |
| [Agents.Fork](agents_fork/Agents.Fork.csproj) |  |
| [Agents.Interfaces](agents_interfaces/Agents.Interfaces.csproj) |  |
| [Agents.Messaging](agents_messaging/Agents.Messaging.csproj) |  |
| [Agents.Output](agents_output/Agents.Output.csproj) |  |
| [Agents.Pipeline](agents_pipeline/Agents.Pipeline.csproj) |  |
| [Agents.Services.Support](agents_services_support/Agents.Services.Support.csproj) |  |
| [Agents.Spawn](agents_spawn/Agents.Spawn.csproj) |  |
| [Agents.Swarm](agents_swarm/Agents.Swarm.csproj) |  |
| [Agents.Team.Serialization](agents_team_serialization/Agents.Team.Serialization.csproj) |  |
| [Agents.Team](agents_team/Agents.Team.csproj) |  |
| [Agents.Tests](agents_tests/Agents.Tests.csproj) | 测试 |
| [Agents.Worktree.Lifecycle](agents_worktree_lifecycle/Agents.Worktree.Lifecycle.csproj) |  |
| [Agents.Worktree.Pipeline](agents_worktree_pipeline/Agents.Worktree.Pipeline.csproj) |  |
| [Agents](agents/Agents.csproj) |  |
| [Llm](core/Llm.csproj) |  |
| [Llm.Tests](llm_tests/Llm.Tests.csproj) | 测试 |
| [Reasoning.Tests](reasoning_tests/Reasoning.Tests.csproj) | 测试 |
| [Reasoning](reasoning/Reasoning.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/llm/module.slnx
dotnet pack modules/llm/module.slnx -o ./nuget-feed
```
