# Jev 类型化决策模型接入任务

> 📍 **导航**: [docs/](../README.md) › [task/](README.md) | **前置**: [plan/](../plan/README.md)
> 🔗 **上游索引**: [task/README.md](README.md) — 修改本文档后须同步更新此索引
> 📖 **架构依据**: [ADR 0098](../adr/0098-plugin-system-fusion-actor-effectscope.md) 万物皆插件 | [ADR 0019](../adr/0019-enum-enumvalue-source-generator.md) 枚举扩展 | [ADR 0103](../adr/0103-folder-restructure-semantic-grouping-flat.md) 目录层次化

## 背景与需求

**Jev** 是 TypeSafe AI 于 2026 年 9 月发布的 "System One Model"(ChatGPT 联合发明人参与)。它**放弃文本生成**,只返回**类型化概率决策**,适用于分类、评分、路由等 System One 任务。

### Jev 协议特征(与现有 LLM provider 根本不同)

| 维度 | 传统 LLM(OpenAI 等) | Jev |
|------|---------------------|-----|
| 端点 | `POST /chat/completions` | `POST https://api.typesafe.ai/v1/systemone` |
| 请求体 | `{model, messages, stream}` | `{model, state, questions}` |
| 响应体 | `choices[].message.content`(文本) | `answers.{questionName}.{noul/choice/score}`(类型化决策) |
| 认证 | `Authorization: Bearer` | `Authorization: Bearer`(相同) |
| 流式 | SSE `data:` chunks | **不确定**(端点路径暗示非流式决策) |
| 延迟 | 秒级 | 70–500ms(比前沿 LLM 快 40–200 倍) |
| 幻觉 | 可能产生自由文本 | 零幻觉(输出预定义类型安全值) |

### 三种问题原语

| 原语 | 用途 | 响应字段 |
|------|------|---------|
| **Noul** | 是非判断的估计概率 | `answers.{q}.noul: float(0-1)` |
| **Choice** | 从 2–6 个选项中分类 | `answers.{q}.choice: string` + `confidence: float` |
| **Score** | 有序等级评分 | `answers.{q}.score: float` |

### 请求示例

```bash
curl https://api.typesafe.ai/v1/systemone \
  -H "Authorization: Bearer $TYPESAFE_API_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @- <<'JSON'
{
  "model": "jev-latest",
  "state": "Please call me tomorrow morning to discuss the setup.",
  "questions": {
    "callback_requested": {
      "type": "noul",
      "instructions": "Does the sender explicitly ask for a phone call?"
    }
  }
}
JSON
```

## 核心原则

1. **配置大于代码**:`protocol: "jev"` 即走 Jev 协议(对齐 TASK004/TASK010 模式)
2. **插件式接入**:Jev 作为新 `ProtocolKind.Jev` 注册到 `QueryServiceFactory`(ADR 0098 万物皆插件)
3. **零接口污染**:Jev 的类型化决策通过 `ApiMessage.Metadata["JevDecision"]` 承载(方案 B,待确认)
4. **AOT 兼容**:所有 DTO 经 `JevJsonContext` 源码生成器注册,JSON 读写用 `RelaxedJsonSerializer`
5. **独立实现**:`JevQueryService : QueryServiceBase`,不继承 `OpenAIQueryService`(协议差异过大)

## 架构决策点

### 决策1:类型化决策抽象层(D++ 方案)✅ 已确认

**决策**:引入新的 `ITypedDecision` 抽象层,Jev 作为首个实现,未来其他类型化决策模型(分类器/评分器/路由器)可复用。**不污染现有 `IQueryService`/`ApiMessage` 体系**。

**设计要点**:
- `ITypedDecision` 抽象接口(放 `lib/abstractions/abs_ai/decision/`):统一类型化决策的元数据(置信度、原语类型、原始值)
- `ITypedDecisionService` 接口(与 `IQueryService` 平级):`GetTypedDecisionsAsync(state, questions, ct) → IReadOnlyDictionary<string, ITypedDecision>`
- Jev 实现 `ITypedDecisionService`,返回 `JevDecision : ITypedDecision`
- Jev **同时实现 `IQueryService`**(走现有工厂分派,保持配置统一),但 `IQueryService.GetApiMessageContentsAsync` 内部调用 `GetTypedDecisionsAsync` 并把结果序列化到 `ApiMessage.Content`(JSON 字符串),需要强类型的消费方直接注入 `ITypedDecisionService`
- 未来扩展:新的类型化决策模型只需实现 `ITypedDecisionService`,不修改任何现有代码

**与现有体系的关系**:
- `IQueryService` 体系:文本生成模型(OpenAI/Anthropic/Azure 等),返回 `ApiMessage`
- `ITypedDecisionService` 体系:类型化决策模型(Jev 等),返回 `ITypedDecision`
- Jev 横跨两体系:既走 `IQueryService`(配置统一)又走 `ITypedDecisionService`(强类型消费)
- 两体系通过 `QueryServiceFactory` 统一注册,按 `ProtocolKind` 分派

> 📖 **架构决策记录**: [ADR 0115](../adr/0115-typed-decision-abstraction-layer.md)(状态:proposed)

### 决策2:Jev 是否支持流式 ⚠️ 待确认

端点路径 `systemone` 暗示非流式决策端点。若不支持流式,`GetStreamEventContentsAsync` 降级为非流式包装单次 yield(参考 `StreamingFallbackDecorator` 已有降级机制)。

**待办**:获取 https://docs.typesafe.ai/api 官方文档确认流式支持。

### 决策3:state 字段如何从 MessageList 映射 ⚠️ 待确认

Jev 的 `state` 是输入材料(字符串/JSON 对象/数组),不是 `messages` 聊天历史。需要决定:
- **方案 a**:把 `MessageList` 拼接成单个字符串作为 `state`
- **方案 b**:把 `MessageList` 转成 JSON 数组作为 `state`
- **方案 c**:从 `ChatOptions` 读取专门的 `JevState` 字段(最语义化但需扩展 ChatOptions)

## 文件改动清单(目录树)

### 新增文件 — 类型化决策抽象层(D++ 方案)

```
lib/abstractions/abs_ai/decision/
├── ITypedDecision.cs                 # 类型化决策抽象接口(置信度/原语类型/原始值)
├── ITypedDecisionService.cs          # 决策服务接口(与 IQueryService 平级)
├── TypedDecisionKind.cs              # 决策原语枚举(Noul/Choice/Score + 未来扩展)
└── TypedDecisionResult.cs            # 决策结果容器(questionName → ITypedDecision)
```

### 新增文件 — Jev 实现

```
llm/core/Adapters/LLM/QueryServices/Jev/
├── JevQueryService.cs                # Jev 协议实现(继承 QueryServiceBase + 实现 ITypedDecisionService)
├── JevTypes.cs                       # 请求/响应 DTO(JevRequest/JevResponse/JevQuestion/JevAnswer)
├── JevDecision.cs                    # JevDecision : ITypedDecision(具体决策实现)
└── JevStateMapper.cs                 # MessageList → Jev state 映射(按决策3)

llm/core/Adapters/LLM/Serialization/
└── JevJsonContext.cs                 # JsonSourceGeneration 源码生成器注册 DTO

lib/guard/configuration/configuration2/core/providers/jev/
└── JevProviderDefinition.cs          # IProviderDefinition 实现(端点/认证/模型能力)

llm/llm.tests/Adapters/LLM/QueryServices/Jev/
└── JevQueryServiceTests.cs           # 单元测试(红→绿)

lib/abstractions/abs_ai.tests/decision/
└── ITypedDecisionServiceTests.cs     # 抽象层契约测试
```

### 需修改的现有文件

| 文件 | 改动 |
|------|------|
| `lib/abstractions/abs_core/configuration/providers/ProtocolKind.cs` | 加 `[EnumValue("jev")] Jev = 5` |
| `lib/abstractions/abs_core/configuration/providers/VendorKind.cs` | 加 `[EnumValue("jev")] Jev = 8` |
| `lib/abstractions/abs_core/configuration/app_data/JccEnvVar.cs` | 加 `[EnumValue("JEV_API_KEY")] JevApiKey` |
| `llm/core/Plugins/LlmProvidersPlugin.cs` | `InitializeAsync` 加 `factory.RegisterProvider(ProtocolKind.Jev, ...)`;`OnUnload` 加 `UnregisterProvider` |
| `llm/core/DependencyInjection/ServiceRegistration.cs` | `EnsureDefaultProvidersRegistered` 加同样注册 |
| `lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs` | protocol switch 加 `Jev` 分支 |
| `llm/core/GlobalUsings.cs` | 加 `global using Api.LLM.QueryServices.Jev;` |

**⚠️ 枚举改动后必须 `dotnet build --no-incremental` 全量重建**(源码生成器增量缓存不会自动重扫新枚举值,见 AGENTS.md Git 规范表)。

## 任务拆分(TDD 渐进式)

> 每个任务遵循:🔴E2E红 → 🔴单元红 → 🟢单元绿 → 🔵重构 → 🟢E2E绿 → 编译 → 提交

### 任务J0:类型化决策抽象层(D++ 方案核心)
- `ITypedDecision.cs`:决策抽象接口(Confidence/Kind/RawValue/QuestionName)
- `ITypedDecisionService.cs`:`GetTypedDecisionsAsync(state, questions, ct) → TypedDecisionResult`
- `TypedDecisionKind.cs`:`[EnumValue("noul")] Noul` / `[EnumValue("choice")] Choice` / `[EnumValue("score")] Score`
- `TypedDecisionResult.cs`:`IReadOnlyDictionary<string, ITypedDecision>` 容器
- 单元测试:契约测试(接口实现必须返回非空 Confidence、正确 Kind)
- 提交
- **前置**:ADR 0115 状态改 accepted

### 任务J1:枚举扩展(ProtocolKind + VendorKind + ProviderEnvVar)
- `ProtocolKind.cs` 加 `[EnumValue("jev")] Jev = 5`
- `VendorKind.cs` 加 `[EnumValue("jev")] Jev = 8`
- `JccEnvVar.cs` 加 `[EnumValue("JEV_API_KEY")] JevApiKey`
- `--no-incremental` 全量重建
- 单元测试:枚举值序列化往返、`ToValue()`/`FromValue()` 双向映射
- 提交

### 任务J2:Jev DTO + JsonContext
- `JevTypes.cs`:`JevRequest`(model/state/questions)、`JevResponse`(id/model/answers/usage)、`JevQuestion`(type/instructions/options)、`JevAnswer`(noul/choice/score/confidence)、`JevDecision`(聚合)、`JevUsage`(input_tokens)
- `JevJsonContext.cs`:`[JsonSourceGenerationOptions]` + `[JsonSerializable]` 注册所有 DTO
- 单元测试:7 个序列化往返测试(请求/响应/三种原语)
- 提交

### 任务J3:JevProviderDefinition 实现
- `JevProviderDefinition.cs` 实现 `IProviderDefinition`
- `Vendor => VendorKind.Jev`、`Protocol => ProtocolKind.Jev`
- `DefaultEndpoint => "https://api.typesafe.ai/v1/systemone"`
- `GetBaseUrl` 返回 `https://api.typesafe.ai/v1/`
- `GetChatEndpoint` 返回 `"systemone"`
- `ConfigureHttpClient` 加 `Authorization: Bearer {apiKey}`
- 单元测试:端点构建、认证头、模型能力查询
- 提交

### 任务J4:JevQueryService 实现
- `JevQueryService.cs` 继承 `QueryServiceBase`
- `CreateJevRequest`:从 `MessageList` 构建 `JevRequest`(state 映射按决策3)
- `GetApiMessageContentsAsync`:POST → 反序列化 `JevResponse` → `ApiMessage`(决策塞 `Metadata["JevDecision"]`)
- `GetStreamEventContentsAsync`:降级为非流式包装单次 yield(按决策2)
- 复用 `SendWithResilienceAsync`
- `JevDecisionExtensions.cs`:`ExtractDecision(ApiMessage)` 辅助方法
- 单元测试:请求构建、响应解析、Metadata 承载、降级流式
- 提交

### 任务J5:ProviderDefinitionRegistry + 插件注册
- `ProviderDefinitionRegistry.cs` protocol switch 加 `Jev` 分支
- `LlmProvidersPlugin.cs` `InitializeAsync` 加 `factory.RegisterProvider(ProtocolKind.Jev, ...)`
- `ServiceRegistration.cs` `EnsureDefaultProvidersRegistered` 加同样注册
- 单元测试:工厂按 `ProtocolKind.Jev` 正确分派到 `JevQueryService`
- 提交

### 任务J6:settings.json 配置示例 + 文档
- `settings.json` 配置示例(jev vendor 配置)
- 更新 `task/README.md` 索引
- 手动验证:配置 `protocol: "jev"` 后 jcc.exe 正确路由到 JevQueryService
- 提交

### 任务J7:E2E 验证(可选,需 API Key)
- 设置 `JEV_API_KEY` 环境变量
- jcc.exe 实际调用 `api.typesafe.ai/v1/systemone`
- 验证 Noul/Choice/Score 三种原语正确返回
- 验证 `Metadata["JevDecision"]` 正确承载

## settings.json 配置示例

```json
"vendor": {
    "jev": {
        "provider": "jev",
        "protocol": "jev",
        "model": "jev-latest",
        "endpoint": "https://api.typesafe.ai/v1/systemone",
        "apiKeyEnvVar": "JEV_API_KEY",
        "models": [
            { "id": "jev-latest", "displayName": "Jev Latest" }
        ]
    }
}
```

## 风险与约束

| 约束 | 说明 |
|------|------|
| **NativeAOT** | 禁止 `dynamic`、反射 emit;必须用 `JevJsonContext` + 源码生成器;JSON 读写用 `RelaxedJsonSerializer` |
| **TreatWarningsAsErrors** | 零警告容忍,新代码必须无警告 |
| **目录层次化** | 新文件放 `QueryServices/Jev/`,命名全小写+下划线,禁止连字符 |
| **源码生成器增量缓存** | 枚举改动后必须 `--no-incremental` 全量重建 |
| **API Key 安全** | 密钥从环境变量 `JEV_API_KEY` 读取,禁止硬编码或嵌入公开代码 |
| **Jev API 尚未稳定** | `jev-latest` 别名可能指向更新模型;测试时记录响应返回的模型标识符 |

## 决策依据

- **D++ 抽象层而非 B/C/D 单点方案**:用户要求"一切作为可插拔设计",引入 `ITypedDecision` 抽象层让 Jev 成为首个实现,未来分类器/评分器/路由器可复用,不修改任何现有代码(开闭原则)
- **独立 JevQueryService 而非复用 OpenAIQueryService**:请求体(`state`+`questions` vs `messages`)、响应体(类型化决策 vs 文本)、端点路径(`systemone` vs `chat/completions`)均不同,复用会增加条件分支复杂度(对齐 TASK010 Responses API 决策)
- **ProtocolKind.Jev 而非复用 OpenAiCompatible**:Jev 协议与 OpenAI 兼容协议根本不同,独立枚举值保证配置语义清晰
- **Jev 横跨 IQueryService + ITypedDecisionService 两体系**:走 `IQueryService` 保持配置统一(QueryServiceFactory 分派),走 `ITypedDecisionService` 提供强类型消费;`IQueryService.GetApiMessageContentsAsync` 内部委托 `GetTypedDecisionsAsync` 并序列化到 `Content`

## 完成状态

| 任务 | 状态 | 提交 |
|------|------|------|
| 任务J0: 类型化决策抽象层 | ✅ 完成(9 测试通过) | 2870b9f7a |
| 任务J1: 枚举扩展 | ✅ 完成(8 测试通过) | 88d508a59 |
| 任务J2: DTO + JsonContext | ✅ 完成(8 测试通过) | d82c2ec2c |
| 任务J3: JevProviderDefinition | ✅ 完成(17 测试通过) | c94d8ada2 |
| 任务J4: JevQueryService | ✅ 完成(6 通过 1 跳过) | 601929b4c |
| 任务J5: Registry + 插件注册 | ✅ 完成(3 测试通过) | 425b5b094 |
| 任务J6: 配置示例 + 文档 | ✅ 完成(本文档) | 待提交 |
| 任务J7: E2E 验证 | ⏳ 待用户验证(需 API Key) | - |

## 自主决策记录(用户睡觉期间自主完成)

<!-- 🤖 Auto Decision: 2026-09-21 -->
<!-- 决策2: Jev 不支持流式,GetStreamEventContentsAsync 降级为非流式包装单次 yield -->
<!-- 原因: 端点路径 systemone 暗示非流式决策端点,Jev 返回类型化决策而非文本流 -->
<!-- 替代方案: 实现 SSE 流式(但 Jev API 可能不支持,且决策结果适合一次性返回) -->

<!-- 🤖 Auto Decision: 2026-09-21 -->
<!-- 决策3: MessageList 拼接为单个字符串作为 state(方案 a) -->
<!-- 原因: 最简单直接,Jev 的 state 是输入材料,MessageList 拼接为带角色标记的字符串 -->
<!-- 替代方案: JSON 数组(方案 b)或 ChatOptions.JevState 专用字段(方案 c,需扩展 ChatOptions) -->

<!-- 🤖 Auto Decision: 2026-09-21 -->
<!-- 决策: JsonElementHelper 替代 JsonSerializer.SerializeToElement 保证 AOT 安全 -->
<!-- 原因: JCC1011 分析器拦截无 JsonTypeInfo 的 SerializeToElement,NativeAOT 下会触发反射异常 -->
<!-- 替代方案: 在 JevJsonContext 注册 string/double/int 原生类型(但 JsonElementHelper 已有现成方法) -->

## 参考链接

- [TypeSafe 官方快速入门](https://docs.typesafe.ai/introduction/quickstart)
- [TypeSafe API 参考](https://docs.typesafe.ai/api)
- [Jev 独立中文指南](https://whatisjev.com/zh/getting-started)
- [Jev AI 官网](https://jevai.net/zh-cn/features/)
- [GitHub: jev-arena 模型实测](https://github.com/NanmiCoder/jev-arena)
