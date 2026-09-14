# 子代理模型 inherit 关键字与 Bedrock 区域前缀补齐计划

## 背景

对齐 TS 原版 `src/utils/model/agent.ts` 的 `getAgentModel` 设计,补齐 w2 项目缺失的两项能力:

| 缺失项 | TS 原版 位置 | w2 现状 | 价值 |
|--------|-----------------|---------|------|
| `inherit` 关键字 | `agent.ts:80-88` | 用 `null` 隐式继承,无显式关键字 | 中 — 配置可读性 |
| Bedrock 跨区域前缀继承 | `agent.ts:50-67` + `bedrock.ts` | 完全缺失 | 中 — Bedrock 用户必需 |

## TS 原版 设计要点

### inherit 关键字
- `getDefaultSubagentModel()` 返回 `'inherit'`
- `getAgentModel` 中:若 `agentModelWithExp === 'inherit'`,调用 `getRuntimeMainLoopModel` 解析父级模型
- AgentJsonSchema 对 `inherit` 做小写归一化
- `getAgentModelDisplay`: `inherit` → "Inherit from parent"

### Bedrock 跨区域前缀
- `BEDROCK_REGION_PREFIXES = ['us', 'eu', 'apac', 'global']`
- `getBedrockRegionPrefix(modelId)`:从模型 ID 或 ARN 提取区域前缀
- `applyBedrockRegionPrefix(modelId, prefix)`:给模型 ID 应用区域前缀
- `isFoundationModel(modelId)`:以 `anthropic.` 开头
- `extractModelIdFromArn(modelId)`:从 ARN 提取最后一段
- 在 `getAgentModel` 中:若父模型有区域前缀且 provider 是 bedrock,子代理解析后的模型也应用相同前缀(除非子代理已显式指定区域前缀)

## 补齐方案

### 任务1: inherit 关键字支持

**修改文件**:
1. `core/execution/Brain/src/Prompts/Core/SystemPromptProviderOptions.cs`
   - 添加 `GetDefaultSubagentModel()` 返回 `"inherit"`
   - 添加 `IsInheritKeyword(string? model)` 判断(不区分大小写)
   - 添加 `GetAgentModelDisplay(string? model)` 显示文本
2. `core/ai/Agents/src/Services/Spawn/Unified/ContextSetupMiddleware.cs`
   - 修改模型解析:支持 `inherit` 关键字,解析为父级模型
   - 父级模型从 `_subAgentContextAccessor.Current?.CacheSafeParams?.ModelId` 获取
3. `core/ai/Agents/src/Services/Support/AgentDefinitionProvider.cs`
   - `ParseDefinitionFile` 中对 `inherit` 做小写归一化
4. `core/ai/Agents/tests/Unit/Agents/ContextSetupMiddlewareSubagentModelTests.cs`
   - 添加 inherit 关键字测试用例

### 任务2: Bedrock 跨区域前缀继承

**修改文件**:
1. `foundation/Abstractions/00-core/Configuration/Providers/VendorKind.cs`
   - 添加 `Bedrock` 枚举值
2. `foundation/Abstractions/00-core/Configuration/Llm/BedrockModelHelper.cs` (新建)
   - `ExtractModelIdFromArn`
   - `GetBedrockRegionPrefix`
   - `ApplyBedrockRegionPrefix`
   - `IsFoundationModel`
   - `RegionPrefixes` 常量
3. `core/ai/Agents/src/Services/Spawn/Unified/ContextSetupMiddleware.cs`
   - 注入 `IModelConfigLoader`
   - 应用 Bedrock 区域前缀继承
4. `foundation/Abstractions/00-core/Configuration/Llm/tests/BedrockModelHelperTests.cs` (新建)
   - 完整单元测试

## 优先级链(对齐 TS 原版)

| 优先级 | 来源 | w2 实现 |
|--------|------|---------|
| 1 | `JCC_SUBAGENT_MODEL` 环境变量 | ✅ 已实现 |
| 2 | `AgentSpawnOptions.Model` (调用时覆盖) | ✅ 已实现 |
| 3 | `AgentDefinition.ModelName` (定义文件) | ✅ 已实现 |
| 4 | `inherit` 关键字 → 父级模型 | ⬜ 本次补齐 |

Bedrock 区域前缀在优先级 2/3 解析后应用(若 provider 是 bedrock 且父模型有前缀)。

## 验证

- `dotnet build foundation/Abstractions/Abstractions.csproj -c Debug`
- `dotnet build core/execution/Brain/Brain.csproj -c Debug`
- `dotnet build core/ai/Agents/Agents.csproj -c Debug`
- `dotnet test` 运行新增单元测试

<!-- 🤖 Auto Decision: 2026-08-19 -->
<!-- 决策: inherit 用静态方法判断而非枚举,Bedrock 前缀用独立 Helper 类 -->
<!-- 原因: 避免侵入 AgentDefinition 模型,Bedrock 逻辑可独立测试 -->
<!-- 替代方案: 把 inherit 作为 ModelAlias 枚举值 — 否决,因模型字段是 string? -->
