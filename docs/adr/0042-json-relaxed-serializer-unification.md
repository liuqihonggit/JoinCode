# 0042. JSON 序列化统一收口 — RelaxedJsonSerializer 单一入口

- 状态：accepted
- 日期：2026-08-30
- 决策者：项目架构组

## 背景

项目有 94 个 `JsonSerializerContext` 派生类分散在七层架构中，JSON 序列化存在三个问题：

1. **中文转义**：`JsonSourceGenerationOptions` attribute 无法引用静态 `Encoder` 属性，导致默认用 `JavaScriptEncoder.Default` 把中文输出为 `\uXXXX` 转义，配置文件人类不可读。
2. **命名策略不一致**：部分 Context 设了 `PropertyNamingPolicy = CamelCase`，部分没设（PascalCase），同一配置文件混用 camelCase 和 PascalCase 字段名。
3. **重复模式**：`ConfigJsonOptions`（Guard）和 `RelaxedJsonSerializer`（Abstractions）做相同的事——注入 `UnsafeRelaxedJsonEscaping` Encoder，维护两套代码。

## 决策

### 1. `RelaxedJsonSerializer` 为所有写文件 JSON 序列化的单一入口

```csharp
// ✅ 统一入口
var json = RelaxedJsonSerializer.Serialize(data, XxxJsonContext.Default);
var json = RelaxedJsonSerializer.SerializeIndented(data, IndentedContext.Default);
var json = RelaxedJsonSerializer.SerializeCompact(data, CompactContext.Default);
```

位于 `foundation/Abstractions/00-core/Core/Utils/RelaxedJsonSerializer.cs`，供所有层复用。

### 2. 所有写入本地文件的 `JsonSerializerContext` 必须声明 `PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase`

```csharp
// ✅ 写文件的 Context
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(MyConfig))]
public partial class MyConfigJsonContext : JsonSerializerContext;
```

**例外**（不设 CamelCase，合理）：
- **网络协议 Context**：OpenAI/Anthropic 等 API 使用 SnakeCaseLower（`prompt_tokens`），设 CamelCase 会破坏协议
- **显式 `[JsonPropertyName]` 覆盖**：所有属性已用 `[JsonPropertyName("xxx")]` 指定 JSON 名，命名策略无影响
- **仅反序列化 Context**：只读不写，不产生输出
- **仅内存/HTTP 载荷 Context**：不写入本地文件

### 3. `RelaxedOptions` 按 Context 缓存（`ConditionalWeakTable`）

```csharp
private static readonly ConditionalWeakTable<JsonSerializerContext, JsonSerializerOptions> s_cache = new();

public static JsonSerializerOptions RelaxedOptions(this JsonSerializerContext context)
    => s_cache.GetValue(context, static c =>
        new(c.Options) { TypeInfoResolver = c, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
```

同一 Context 多次调用返回同一 `JsonSerializerOptions` 实例，避免重复分配。

### 4. 删除 `ConfigJsonOptions`，合并到 `RelaxedJsonSerializer`

`ConfigJsonOptions.SerializeIndented/Compact` 的 7 处调用全部改为 `RelaxedJsonSerializer.SerializeIndented/Compact`。未来改 Encoder 策略只需改 `RelaxedJsonSerializer` 一处。

### 5. Deserialize 不统一

340 处 `JsonSerializer.Deserialize` 保持直接调用 `JsonSerializer.Deserialize(json, Context.Default.XxxTypeInfo)`。原因：Deserialize 不需要 Encoder（反序列化时 `\uXXXX` 自动解码为中文），强行包装只增加间接调用无收益。

## 替代方案

1. **每个 Context 自带 Encoder 静态字段**：放弃。94 个 Context 各写一遍 `new(options) { Encoder = ... }`，维护成本高，容易遗漏（实际已遗漏 NotebookService）。
2. **用 `JsonSerializerOptions` 全局默认设 Encoder**：放弃。`JsonSerializerOptions` 是 per-instance 的，无全局默认机制；且会影响网络协议 Context（不需要真实中文输出）。
3. **源码生成器自动注入 Encoder**：放弃。`JsonSourceGenerationOptions` 的 attribute 参数限制不允许引用静态属性（`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` 是静态属性），需运行时创建副本。
4. **合并 94 个 Context 为少数几个**：放弃。AOT 要求类型静态 rooted，合并引入跨组件耦合，违反七层架构隔离。
5. **统一 Deserialize 入口**：放弃。Deserialize 不需要 Encoder，340 处改动风险远大于收益。

## 后果

- 正面：所有写文件 JSON 统一 camelCase + 真实中文输出；单一入口 `RelaxedJsonSerializer` 未来改 Encoder 只改一处；`ConditionalWeakTable` 缓存消除重复分配；`ConfigJsonOptions` 重复代码删除
- 负面：`RelaxedJsonSerializer.Serialize<T>(T, JsonSerializerContext)` 每次查 `ConditionalWeakTable`（O(1) 哈希查找，可忽略）；新增 Context 需记得设 `PropertyNamingPolicy = CamelCase`
- 中性：94 个 Context 保持分散（AOT 隔离要求）；340 处 Deserialize 保持直接调用（不需要 Encoder）
