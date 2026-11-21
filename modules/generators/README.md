# generators — 源码生成器集合

> 📍 **导航**: [modules/](../README.md) › generators

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [AotHandler.Generator](aot_handler.generator/AotHandler.Generator.csproj) | 源码生成器 |
| [AotSafety.Generator](aot_safety.generator/AotSafety.Generator.csproj) | 源码生成器 |
| [AotSafety.Shared](aot_safety.shared/AotSafety.Shared.csproj) |  |
| [AotSafety.Tests](aot_safety.tests/AotSafety.Tests.csproj) | 测试 |
| [AppModule.Generator](app_module.generator/AppModule.Generator.csproj) | 源码生成器 |
| [CliOption.Generator](cli_option.generator/CliOption.Generator.csproj) | 源码生成器 |
| [CodeFixes](code_fixes/CodeFixes.csproj) |  |
| [DangerCommand.Generator](danger_command.generator/DangerCommand.Generator.csproj) | 源码生成器 |
| [EnumMetadata.Generator](enum_metadata.generator/EnumMetadata.Generator.csproj) | 源码生成器 |
| [Fsm.Generator.Tests](fsm.generator.tests/Fsm.Generator.Tests.csproj) | 测试 |
| [Fsm.Generator](fsm.generator/Fsm.Generator.csproj) | 源码生成器 |
| [McpToolDispatch.Generator](mcp_tool_dispatch.generator/McpToolDispatch.Generator.csproj) | 源码生成器 |
| [ParamMetadata.Generator](param_metadata.generator/ParamMetadata.Generator.csproj) | 源码生成器 |
| [PromptSection.Generator](prompt_section.generator/PromptSection.Generator.csproj) | 源码生成器 |
| [PromptTemplate.Generator](prompt_template.generator/PromptTemplate.Generator.csproj) | 源码生成器 |
| [Scenario.Generator](scenario.generator/Scenario.Generator.csproj) | 源码生成器 |
| [ToolPrompt.Generator](tool_prompt.generator/ToolPrompt.Generator.csproj) | 源码生成器 |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/generators/module.slnx
dotnet pack modules/generators/module.slnx -o ./nuget-feed
```
