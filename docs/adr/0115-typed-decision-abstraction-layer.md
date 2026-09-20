# 0115. 类型化决策抽象层 — ITypedDecision

- 状态：accepted
- 日期：2026-09-21
- 决策者：用户 + AI

## 背景

项目现有 LLM provider 体系(`IQueryService` → `ApiMessage`)面向**文本生成模型**(OpenAI/Anthropic/Azure/Agnes/Responses),返回 `string? Content` + `Dictionary<string, JsonElement> Metadata`。

2026 年 9 月 TypeSafe AI 发布 **Jev**("System One Model"),它**放弃文本生成**,只返回**类型化概率决策**:

| 原语 | 用途 | 响应 |
|------|------|------|
| Noul | 是非判断概率 | `float(0-1)` |
| Choice | 分类(2-6 选项) | `string` + `confidence` |
| Score | 有序等级评分 | `float` |

Jev 的返回类型与 `ApiMessage.Content`(string)根本不同。需要决定如何承载这种类型化决策,同时考虑未来可能有其他类型化决策模型(分类器/评分器/路由器)接入。

## 决策

引入 **`ITypedDecision` 抽象层**,Jev 作为首个实现,未来其他类型化决策模型可复用,不修改任何现有代码(开闭原则)。

### 架构

```
lib/abstractions/abs_ai/decision/
├── ITypedDecision.cs              # 决策抽象(Confidence/Kind/RawValue/QuestionName)
├── ITypedDecisionService.cs       # 决策服务接口(与 IQueryService 平级)
├── TypedDecisionKind.cs           # 原语枚举(Noul/Choice/Score + 未来扩展)
└── TypedDecisionResult.cs         # 结果容器(questionName → ITypedDecision)

llm/core/Adapters/LLM/QueryServices/Jev/
├── JevQueryService.cs             # 实现 IQueryService + ITypedDecisionService(横跨两体系)
└── JevDecision.cs                 # JevDecision : ITypedDecision
```

### 两体系关系

| 体系 | 接口 | 返回 | 适用模型 |
|------|------|------|---------|
| 文本生成 | `IQueryService` | `ApiMessage`(string Content) | OpenAI/Anthropic/Azure/Agnes/Responses |
| 类型化决策 | `ITypedDecisionService` | `TypedDecisionResult`(ITypedDecision 字典) | Jev + 未来分类器/评分器 |

**Jev 横跨两体系**:
- 实现 `IQueryService`:走 `QueryServiceFactory` 统一分派,保持配置统一(`protocol: "jev"`)
- 实现 `ITypedDecisionService`:提供强类型消费,需要决策的消费方直接注入 `ITypedDecisionService`
- `IQueryService.GetApiMessageContentsAsync` 内部委托 `GetTypedDecisionsAsync`,把决策序列化为 JSON 塞 `ApiMessage.Content`(供不需要强类型的消费方使用)

### 接口签名(草案)

```csharp
// ITypedDecision
public interface ITypedDecision {
    string QuestionName { get; }         // 问题标识
    TypedDecisionKind Kind { get; }      // 原语类型(Noul/Choice/Score)
    double Confidence { get; }           // 置信度(0-1)
    object RawValue { get; }             // 原始值(float for Noul/Score, string for Choice)
}

// ITypedDecisionService
public interface ITypedDecisionService {
    Task<TypedDecisionResult> GetTypedDecisionsAsync(
        string state, IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken cancellationToken = default);
}
```

## 替代方案

1. **方案 B — Metadata 扩展**:决策塞 `ApiMessage.Metadata["JevDecision"]` 为 JsonElement。零接口改动,符合现有扩展模式(OpenAI 已用 `Metadata["Usage"]`)。**放弃原因**:消费方需手动 `decision.Deserialize<JevDecision>()`,损失类型安全;且 Jev 特有逻辑侵入 Metadata 通用机制,未来每个类型化决策模型都要约定自己的 Metadata key,缺乏统一抽象。

2. **方案 C — 扩展 ApiMessage**:加 `JevDecision? TypedDecision` 强类型字段。编译时类型安全。**放弃原因**:破坏性改动,影响所有 provider(需为非 Jev provider 返回 null);且 `ApiMessage` 是文本生成体系的核心 DTO,塞类型化决策字段违反单一职责;每新增一种类型化决策模型都要加字段,不可扩展。

3. **方案 D — 新接口 IJevQueryService**:继承 `IQueryService` 加 `GetTypedDecisionsAsync`。类型安全 + 不污染基类。**放弃原因**:接口绑定 Jev 特定类型,未来其他类型化决策模型(分类器/评分器)需各自定义 `IXxxQueryService`,无法复用;工厂需为每种类型化决策模型特殊分派,DI 复杂度线性增长。

4. **方案 D++ — 本决策**:引入 `ITypedDecision` 抽象层,所有类型化决策模型实现统一 `ITypedDecisionService`。**选择原因**:开闭原则(新模型不修改现有代码)、类型安全(强类型 `ITypedDecision`)、统一抽象(未来分类器/评分器/路由器复用)、与现有体系解耦(`IQueryService` 不污染)。

## 后果

- **正面**:
  - 未来类型化决策模型(分类器/评分器/路由器)只需实现 `ITypedDecisionService`,零修改现有代码
  - 消费方可按需注入 `ITypedDecisionService` 获取强类型决策,或注入 `IQueryService` 获取统一 `ApiMessage`
  - Jev 的决策逻辑与文本生成体系完全解耦,可独立演进
  - `TypedDecisionKind` 枚举 + `[EnumValue]` 源码生成器,新增原语类型自动生成常量
- **负面**:
  - 新增抽象层增加一层间接性(但 `ITypedDecision` 接口极简,4 个属性)
  - Jev 横跨两体系,`JevQueryService` 需同时实现两个接口(但 `IQueryService` 方法内部委托 `ITypedDecisionService`,实现量不增加)
  - 消费方需理解何时用 `IQueryService` 何时用 `ITypedDecisionService`(但配置统一,`protocol: "jev"` 即走 Jev)
- **中性**:
  - `ITypedDecision.RawValue` 是 `object` 类型,AOT 下需注意(实际实现用具体类型 + JsonElement 承载,AOT 安全)

## 相关

- 上游:[0098](0098-plugin-system-fusion-actor-effectscope.md) 万物皆插件(Jev 通过 `QueryServiceFactory` 注册)
- 上游:[0019](0019-enum-enumvalue-source-generator.md) 枚举扩展(`TypedDecisionKind` 用 `[EnumValue]`)
- 上游:[0103](0103-folder-restructure-semantic-grouping-flat.md) 目录层次化(抽象层放 `lib/abstractions/abs_ai/decision/`)
- 任务:[TASK026](../task/TASK026-Jev类型化决策模型接入任务.md) Jev 接入任务清单
- 对比:[TASK010](../task/TASK010-ResponsesAPI支持任务.md) Responses API 独立 ProtocolKind 决策(同类先例)
