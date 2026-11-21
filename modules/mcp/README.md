# mcp — MCP协议+工具分发

> 📍 **导航**: [modules/](../README.md) › mcp

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [McpToolDispatch.Tests](mcp_tool_dispatch.tests/McpToolDispatch.Tests.csproj) | 测试 |
| [McpToolDispatch](mcp_tool_dispatch/McpToolDispatch.csproj) |  |
| [Mcp.Protocol.Contracts](mcp.protocol_contracts/Mcp.Protocol.Contracts.csproj) |  |
| [Mcp.Protocol](mcp.protocol/Mcp.Protocol.csproj) |  |
| [Mcp.Tests](mcp.tests/Mcp.Tests.csproj) | 测试 |
| [Mcp.ToolHandlers](mcp.tool_handlers/Mcp.ToolHandlers.csproj) |  |
| [Mcp](mcp/Mcp.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/mcp/module.slnx
dotnet pack modules/mcp/module.slnx -o ./nuget-feed
```
