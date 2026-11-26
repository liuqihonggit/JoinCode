# generators — 源码生成器集合

> 📍 **导航**: [modules/](../README.md) › generators

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [AotHandler.Generator](aot_handler_generator/AotHandler.Generator.csproj) | 源码生成器 |
| [AotSafety.Generator](aot_safety_generator/AotSafety.Generator.csproj) | 源码生成器 |
| [AotSafety.Shared](aot_safety_shared/AotSafety.Shared.csproj) |  |
| [AotSafety.Tests](aot_safety_tests/AotSafety.Tests.csproj) | 测试 |
| [AppModule.Generator](app_module_generator/AppModule.Generator.csproj) | 源码生成器 |
| [CliOption.Generator](cli_option_generator/CliOption.Generator.csproj) | 源码生成器 |
| [CliPermission.Analyzer.Tests](cli_permission_analyzer_tests/CliPermission.Analyzer.Tests.csproj) | 测试 |
| [CliPermission.Analyzer](cli_permission_analyzer/CliPermission.Analyzer.csproj) |  |
| [CodeFixes](code_fixes/CodeFixes.csproj) |  |
| [DangerCommand.Generator](danger_command_generator/DangerCommand.Generator.csproj) | 源码生成器 |
| [EnumMetadata.Generator](enum_metadata_generator/EnumMetadata.Generator.csproj) | 源码生成器 |
| [Fsm.Generator.Tests](fsm_generator_tests/Fsm.Generator.Tests.csproj) | 测试 |
| [Fsm.Generator](fsm_generator/Fsm.Generator.csproj) | 源码生成器 |
| [McpToolDispatch.Generator](mcp_tool_dispatch_generator/McpToolDispatch.Generator.csproj) | 源码生成器 |
| [ParamMetadata.Generator](param_metadata_generator/ParamMetadata.Generator.csproj) | 源码生成器 |
| [PromptSection.Generator](prompt_section_generator/PromptSection.Generator.csproj) | 源码生成器 |
| [PromptTemplate.Generator](prompt_template_generator/PromptTemplate.Generator.csproj) | 源码生成器 |
| [Scenario.Generator](scenario_generator/Scenario.Generator.csproj) | 源码生成器 |
| [ToolPrompt.Generator](tool_prompt_generator/ToolPrompt.Generator.csproj) | 源码生成器 |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/generators/module.slnx
dotnet pack modules/generators/module.slnx -o ./nuget-feed
```
