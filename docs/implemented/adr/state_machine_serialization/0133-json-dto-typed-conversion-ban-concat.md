# 0133. JSON DTO 双向转换 — 禁止手写 JSON 字符串拼接

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引
> 🔗 **相关 ADR**: [0042](0042-json-relaxed-serializer-unification.md)（序列化统一入口）、[0087](0087-batch-replace-csharp-source-rules.md)（批量替换规范）

- 状态：accepted
- 日期：2026-10-08
- 决策者：项目架构组

## 背景

项目在 GitHub 工具链、Bridge 通信层、LLM Responses 查询服务、CLI 崩溃报告等多处存在**手写 JSON 字符串拼接**，表现为以下反模式：

1. **`StringBuilder` 拼接 JSON**：`sb.Append("{\"name\":\"").Append(EscapeJsonString(x)).Append("\"}")` — 手写转义、易漏逗号/括号、无类型安全
2. **内插字符串拼接 JSON**：`$"{{\"title\":\"{title}\",\"body\":\"{body}\"}}"` — `}` 与内插占位符冲突，`$$$$$"""..."""` 前缀数量需手动计算，GraphQL 查询末尾多个 `}}}}}` 极易出错
3. **`EscapeJsonString` / `JsonEncode` 手写转义方法**：每个模块各写一遍，行为不一致（有的漏转义 `\n`，有的漏转义 `\t`），删除后才发现 5+ 处重复
4. **`JsonDocument.Parse` + `TryGetProperty` + `GetString()` 链式提取**：73 处 Summarize/Parse 方法用此模式，字段名用字符串字面量（`"full_name"`），无编译期检查，重构改字段名需全文搜索
5. **中文转义**：默认 `JavaScriptEncoder.Default` 把中文输出为 `\uXXXX`，配置文件人类不可读（[0042] 已解决写入侧，但手写拼接绕过了序列化器）

**系统性问题**：GraphQL mutation 中 `$$$$$"""...{createdAt}}}}}"""` 内插原始字符串的 `$` 前缀数量与结尾 `}` 数量不匹配，在 `GitHubToolHandlers.P4.cs`、`GraphQLEdit.cs` 等多处反复踩坑，每次调整 `$` 数量是治标不治本。

## 决策

### 1. 所有 JSON 处理必须用 DTO + JsonContext 双向转换

**禁止**手写 JSON 字符串拼接（`StringBuilder` 拼 JSON、`$"{{...}}"` 内插 JSON、`EscapeJsonString` 手写转义）。

```csharp
// ✅ 请求体：DTO + JsonSerializer.Serialize
var request = new MilestoneRequest { Title = title, Description = desc };
var json = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.MilestoneRequest);

// ✅ 响应解析：DTO + JsonSerializer.Deserialize
var response = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.LabelResponse);

// ✅ JsonElement 提取：DTO + Deserialize（替代 TryGetProperty 链）
var label = element.Deserialize(GitHubApiJsonContext.Safe.LabelResponse)!;
return $"{label.Name}: {label.Description}";
```

### 2. GraphQL 分两层处理

GraphQL 请求体 `{"query":"...","variables":{...}}` 本身就是 JSON，分两层：

- **外层 JSON 壳**（`{"query":"...","variables":{...}}` 结构）→ 用 `GraphQLRequest` DTO + `BuildGraphQL` 辅助方法生成，注册到 `GitHubApiJsonContext`
- **内层 GraphQL 查询字符串**（query 字段的值）→ 用普通 `$"..."` 拼接即可，无需转义 `}`

```csharp
// ✅ GraphQL：外层 DTO，内层字符串
var query = $"mutation {{ addSubIssue(input: {{subIssueId: \"{subId}\", issueId: \"{parentId}\"}}) {{ ... }} }}";
var body = BuildGraphQL(query);  // GraphQLRequest DTO → JsonSerializer.Serialize
```

这样分层后**彻底消除** `}` 转义问题——外层由 serializer 负责转义，内层 GraphQL 字符串里的 `}` 是查询语法本身不需要 JSON 转义。

### 3. 中文不能被转义 — 用 `Safe` 属性

`JsonSourceGenerationOptions` 特性不支持 `Encoder` 参数。每个 `JsonSerializerContext` 需提供 `Safe` 静态属性（`Lazy<T>` 创建，带 `UnsafeRelaxedJsonEscaping`）：

```csharp
public partial class GitHubApiJsonContext
{
    private static readonly Lazy<GitHubApiJsonContext> s_safe = new(() =>
        new(new JsonSerializerOptions(s_default.Options)
        {
            TypeInfoResolver = s_default,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    public static GitHubApiJsonContext Safe => s_safe.Value;
}
```

### 4. DTO 注册到 JsonContext 必须为 public

`JsonSerializerContext` 是 public，被引用的 DTO 类必须 public，否则源码生成器报 CS0122/CS0053 可访问性不一致。

### 5. WhenWritingNull 注意事项

`WhenWritingNull` 在序列化时**跳过 null 值**。凡是需要显式发送 null 值来触发 API 的"清除/移除"语义时（如 `remove_milestone` 传 `milestone: null`），不要依赖 DTO + WhenWritingNull 序列化，直接手写 JSON 字符串或改用显式 nullable 包装类型。

**例外**：`remove_milestone` 等"移除"语义的 null 值写入，是唯一允许手写 JSON 的场景。

### 6. 保留 `JsonDocument.Parse` 的场景

以下场景保留 `JsonDocument.Parse` + `Utf8JsonWriter` 动态字段过滤，**不强制**改 DTO：

- **通用 `--json` 参数动态字段过滤**（`FilterJsonFields`）— 字段名运行时决定，无法建模为 DTO
- **`Utf8JsonWriter` + `CopyProperty` 动态 JSON 重写**（如 `SummarizeReleaseList`、`SummarizeRepoList`）— 输出字段集运行时决定
- **`BuildFullProtectionPutBody` JSON 重写** — 输入 JSON 结构不固定，需逐字段拷贝改写

## 规范

### 禁令

| 禁止 | 替代 |
|------|------|
| `StringBuilder` 拼 JSON | DTO + `JsonSerializer.Serialize` |
| `$"{{\"key\":\"{val}\"}}"` 内插 JSON | DTO + `JsonSerializer.Serialize` |
| `EscapeJsonString` / `JsonEncode` 手写转义 | 删除，用 `JsonSerializer`（自动转义） |
| `JsonDocument.Parse` + `TryGetProperty("xxx")` 链式提取 | DTO + `JsonSerializer.Deserialize` |
| `$$$$$"""...}}}}}"""` 内插原始字符串拼 GraphQL | `BuildGraphQL(query)` DTO |
| 字段名字符串字面量 `"full_name"` | DTO 属性 + `[JsonPropertyName]` |

### 正确模式速查

| 场景 | ✅ 推荐 |
|------|---------|
| REST API 请求体 | DTO + `JsonSerializer.Serialize(dto, Context.Safe.DtoType)` |
| REST API 响应解析 | DTO + `JsonSerializer.Deserialize(json, Context.Safe.DtoType)` |
| GraphQL mutation | `BuildGraphQL($"mutation {{ ... }}")` |
| JSON 字段名常量 | `GitHubJsonFields.XXX` 常量类 |
| 动态字段过滤 | `JsonDocument.Parse` + `Utf8JsonWriter` + `CopyProperty`（保留） |
| 显式 null 写入 | 手写 JSON 字符串（唯一例外） |

### 重构验收

| 模块 | 已重构处数 | 验证方式 |
|------|-----------|---------|
| CLI 崩溃报告（Program.cs） | 1处 | 编译通过 + Mcp.Tests 903 通过 |
| 诊断日志（DiagnosticLogRecorder） | 1处 | 编译通过 |
| 关键词注入（KeywordInjectionMiddleware） | 1处 | 编译通过 |
| Web 搜索 Schema（WebService） | 1处 | 编译通过 |
| 图可视化（GraphVisualization） | 1处 | 编译通过 |
| GitHub API 响应解析（73处 Summarize/Parse） | 73处 | 编译通过 + GhCommandResolver 75 通过 |
| GitHub 魔法字符串（253处） | 253处 | `GitHubJsonFields` 常量类 |
| Bridge 通信层 | 11处 | 编译通过 |
| Responses 查询服务 | 6处 | 编译通过 + Llm.Tests 601 通过 |
| **合计** | **348处** | **1936 测试通过，0 失败** |

## 替代方案

1. **保持手写拼接，加分析器检测**：放弃。治标不治本——分析器只能报错不能修复，且手写拼接的转义错误是运行时 bug 不是编译期错误。DTO + JsonContext 提供编译期类型安全，字段名错误直接编译失败。

2. **用 `System.Text.Json.Nodes`（JsonNode DOM）替代 DTO**：放弃。JsonNode 是动态 DOM，无编译期类型检查，字段名仍是字符串字面量，只是消除了手写转义。DTO 提供完整的编译期拦截。

3. **用 `JsonDocument.Parse` + `GetProperty("xxx").GetString()` 替代 DTO**：放弃（已验证不可行）。字段名用字符串字面量无编译期检查，重构改字段名需全文搜索；且 `TryGetProperty` 链冗长易错。DTO 属性访问简洁且类型安全。

4. **合并所有 DTO 到一个巨型 JsonContext**：放弃。AOT 要求类型静态 rooted，合并引入跨组件耦合，违反七层架构隔离（与 [0042] 替代方案4 同理）。

5. **用 `dynamic` + 匿名类型替代 DTO**：放弃。`dynamic` 禁止 NativeAOT（[0002]），匿名类型无法注册到 `JsonSerializerContext`。

6. **GraphQL 用 Go template / jq 等模板引擎**：放弃。引入额外依赖，且 .NET 无原生 Go template 支持。DTO + `BuildGraphQL` 已彻底消除 `}` 转义问题，无需模板引擎。

## 后果

- 正面：
  - **编译期类型安全** — DTO 属性改名/删字段直接编译失败，不再运行时 `KeyNotFoundException`
  - **消除手写转义** — 删除 5+ 个 `EscapeJsonString`/`JsonEncode` 重复方法，`JsonSerializer` 自动处理
  - **消除 GraphQL `}` 转义坑** — `BuildGraphQL` 分层后内层查询字符串的 `}` 不需要 JSON 转义
  - **中文不转义** — `Safe` 属性统一用 `UnsafeRelaxedJsonEscaping`，配置文件人类可读
  - **字段名常量化** — `GitHubJsonFields` 82 个常量，253 处引用，重构改字段名改一处
  - **AOT 兼容** — DTO + JsonContext 是 NativeAOT 的标准模式，零反射
- 负面：
  - **DTO 类膨胀** — 每个 API 端点需定义请求/响应 DTO，类数量增加（但每个 DTO 职责单一，可维护性更优）
  - **JsonContext 注册维护** — 新增 DTO 需注册到对应 JsonContext，遗漏会编译失败（编译期拦截，非运行时）
  - **WhenWritingNull 陷阱** — null 值被跳过，"移除"语义需手写 JSON（已记录为唯一例外）
- 中性：
  - 保留 5 处 `JsonDocument.Parse` + `Utf8JsonWriter` 动态字段过滤（运行时字段集，无法建模为 DTO）
  - `RelaxedJsonSerializer`（[0042]）仍是写文件 JSON 的统一入口，本 ADR 聚焦网络/内存 JSON 的 DTO 化
