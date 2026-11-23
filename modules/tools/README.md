# tools — 辅助/非交付工具

> 📍 **导航**: [modules/](../README.md) › tools

## 项目

<!-- BEGIN MODULE PROJECTS -->
| 项目 | 说明 |
|------|------|
| [jcc_tool_agent](cli_nodes/groups/agent/jcc_tool_agent.csproj) |  |
| [jcc_tool_code](cli_nodes/groups/code/jcc_tool_code.csproj) |  |
| [jcc_tool_file](cli_nodes/groups/file/jcc_tool_file.csproj) |  |
| [jcc_tool_git](cli_nodes/groups/git/jcc_tool_git.csproj) |  |
| [jcc_tool_github](cli_nodes/groups/github/jcc_tool_github.csproj) |  |
| [jcc_tool_mcp](cli_nodes/groups/mcp/jcc_tool_mcp.csproj) |  |
| [jcc_tool_media](cli_nodes/groups/media/jcc_tool_media.csproj) |  |
| [jcc_tool_search](cli_nodes/groups/search/jcc_tool_search.csproj) |  |
| [jcc_tool_shell](cli_nodes/groups/shell/jcc_tool_shell.csproj) |  |
| [jcc_tool_slash](cli_nodes/groups/slash/jcc_tool_slash.csproj) |  |
| [jcc_tool_system](cli_nodes/groups/system/jcc_tool_system.csproj) |  |
| [jcc_tool_tools](cli_nodes/groups/tools/jcc_tool_tools.csproj) |  |
| [jcc_tool_web](cli_nodes/groups/web/jcc_tool_web.csproj) |  |
| [jcc_tool_sample](cli_nodes/sample/jcc_tool_sample.csproj) | 示例 |
| [CtorScanner](ctor_scanner/CtorScanner.csproj) |  |
| [Gh.Tests](gh_tests/Gh.Tests.csproj) | 测试 |
| [gh](gh/gh.csproj) |  |
| [GitHubClient.Tests](github_client_tests/GitHubClient.Tests.csproj) | 测试 |
| [GitHubClient](github_client/GitHubClient.csproj) |  |
| [OnnxEmbedding.Tests](onnx_embedding_tests/OnnxEmbedding.Tests.csproj) | 测试 |
| [OnnxEmbeddingVerify](onnx_embedding_verify/OnnxEmbeddingVerify.csproj) |  |
| [OnnxEmbeddingExe](onnx_embedding/OnnxEmbeddingExe.csproj) |  |
| [Rg.Tests](rg_tests/Rg.Tests.csproj) | 测试 |
| [rg](rg/rg.csproj) |  |
| [SimdVectorVerify](simd_vector_verify/SimdVectorVerify.csproj) |  |
<!-- END MODULE PROJECTS -->

## 用法

```bash
dotnet build modules/tools/module.slnx
dotnet pack modules/tools/module.slnx -o ./nuget-feed
```
