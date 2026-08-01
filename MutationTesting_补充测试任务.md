# 变异测试补充测试任务

## 背景

- 仓库: `liuqihonggit/JoinCode`
- 分支: `w1`
- 触发来源: Mutation Testing #3 (手动运行)
- MCP 项目由其他 Agent 负责补充测试
- 目标: 将其他项目的变异测试覆盖率提升到 **60%**

## 全量变异测试结果

| 项目 | 变异得分 | 状态 | 备注 |
|------|----------|------|------|
| CodeIndex | 32.15% | ✅ | 最高分 |
| Reasoning | 27.53% | ✅ | |
| Brain | 20.23% | ✅ | |
| Dream | 18.85% | ✅ | |
| Clock | 16.69% | ✅ | |
| Vault | 13.86% | ✅ | |
| Bridge | 13.46% | ✅ | |
| Hands | 12.47% | ✅ | |
| Scheduling | 11.58% | ✅ | |
| Llm | 8.86% | ✅ | |
| Agents | 8.33% | ✅ | |
| Eyes | 5.22% | ✅ | |
| McpToolDispatch | 2.31% | ✅ | |
| Browser | 1.23% | ✅ | |
| Composition | 1.12% | ✅ | |
| Mcp | 0.05% | ✅ | 最低分，由其他 Agent 负责 |
| Guard | — | ❌ | AccessViolationException |
| Host | — | ❌ | slnx 路径错误（已修复） |
| Infra | — | ❌ | slnx 路径错误（已修复） |

## 本次任务范围

排除 MCP，负责项目：

1. CodeIndex
2. Reasoning
3. Brain
4. Dream
5. Clock
6. Vault
7. Bridge
8. Hands
9. Scheduling
10. Llm
11. Agents
12. Eyes
13. McpToolDispatch
14. Browser
15. Composition
16. Guard（修复 Stryker 崩溃后再补充）
17. Host（修复 slnx 路径后再补充）
18. Infra（修复 slnx 路径后再补充）

## 工作原则

1. **分项目并行**: 每个 Agent 负责 1-2 个项目，独立推进
2. **增量提交**: 每个项目编译通过并绿测试后，由主 Agent 统一 `git commit`
3. **禁止并行子 Agent 提交**: 子 Agent 只写代码、编译、冒烟，不执行 `git commit`/`git push`
4. **主 Agent 全量测试**: 子 Agent 完成后，主 Agent 跑全量测试与变异测试验证
5. **TDD 铁律**: 针对 surviving mutants 写测试，先红后绿
6. **参考现有模式**: 遵循项目现有测试风格与 `Testing.Common` 工具

## 项目路径映射

| 项目 | 源码 csproj | 测试目录 |
|------|-------------|----------|
| CodeIndex | `core\search\CodeIndex\src\CodeIndex.csproj` | `core\search\CodeIndex\tests\Unit` |
| Reasoning | `core\ai\Reasoning\src\Reasoning.csproj` | `core\ai\Reasoning\tests\Unit` |
| Brain | `core\execution\Brain\src\Brain.csproj` | `core\execution\Brain\tests\Unit` |
| Dream | `services\Dream\src\Dream.csproj` | `services\Dream\tests\Unit` |
| Clock | `composition\Clock\src\Clock.csproj` | `composition\Clock\tests\Unit` |
| Vault | `core\safety\Vault\src\Vault.csproj` | `core\safety\Vault\tests\Unit` |
| Bridge | `services\Bridge\src\Bridge.csproj` | `services\Bridge\tests\Unit` |
| Hands | `core\execution\Hands\src\Hands.csproj` | `core\execution\Hands\tests\Unit` |
| Scheduling | `core\execution\Scheduling\src\Scheduling.csproj` | `core\execution\Scheduling\tests\Unit` |
| Llm | `core\ai\Llm\src\Llm.csproj` | `core\ai\Llm\tests\Unit` |
| Agents | `core\ai\Agents\src\Agents.csproj` | `core\ai\Agents\tests\Unit` |
| Eyes | `services\Eyes\src\Eyes.csproj` | `services\Eyes\tests\Unit` |
| McpToolDispatch | `core\execution\McpToolDispatch\src\McpToolDispatch.csproj` | `core\execution\McpToolDispatch\tests\Unit` |
| Browser | `core\search\Browser\src\Browser.csproj` | `core\search\Browser\tests\Unit` |
| Composition | `composition\Composition\src\Composition.csproj` | `composition\Composition\tests\Unit` |
| Guard | `core\safety\Guard\src\Guard.csproj` | `core\safety\Guard\tests\Unit` |
| Host | `app\JoinCode\JoinCode.csproj` | `tests\Unit\Host.Tests` |
| Infra | `infrastructure\Infrastructure\src\Infrastructure.csproj` | `tests\Unit\Infra.Tests` |

## 下一步

由主 Agent 派发并行子 Agent，每个子 Agent 按以下步骤执行：

1. 读取项目源码与现有测试
2. 分析 surviving mutants（通过 Stryker 报告或源码静态分析）
3. 编写新测试用例
4. 编译对应测试项目（Debug 模式）
5. 运行对应测试项目快速冒烟
6. 报告完成状态与新增测试列表
