# 0138. Web 搜索多供应商统一接口 + 纵深兜底

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-10-18
- 决策者：AI + 用户确认

## 背景

现状 `WebService.SearchAsync`（`modules/cognition/hands/web/services/WebService.cs:53`）强依赖 Anthropic provider：`_providerConfig?.Definition is not { SupportsWebSearch: true }` 直接返回错误 `"Web search is not available for the current provider"`。非 Anthropic provider（OpenAI/华为云/DeepSeek 等）完全无法使用 WebSearch。

用户要求：
1. 接入多个搜索引擎供应商
2. 统一对外 API（上层无感知）
3. 纵深防御：所有供应商都不可用时，让子代理用 WebFetch 抓取 + LLM 整理结果兜底

## 决策

引入 **`ISearchProvider` 统一抽象 + `SearchProviderCoordinator` 多供应商调度 + `SubAgentSearchProvider` 兜底**，三层纵深防御。

### 架构

```
WebService.SearchAsync (对外统一入口，不变)
  ↓
SearchProviderCoordinator (多供应商调度)
  ├─ 按 Priority 顺序遍历可用 providers
  ├─ 第一个 Success 即返回
  ├─ 全失败 → 调 SubAgentSearchProvider 兜底
  └─ 兜底也失败 → 返回聚合错误
  ↓
ISearchProvider (统一抽象)
  ├─ TavilySearchProvider        (Priority=100, TAVILY_API_KEY)
  ├─ SerperSearchProvider        (Priority=200, SERPER_API_KEY, Google 结果)
  ├─ BraveSearchProvider         (Priority=300, BRAVE_API_KEY)
  ├─ SearXNGSearchProvider       (Priority=400, SEARXNG_URL, 自托管)
  ├─ AnthropicSearchProvider     (Priority=500, 现有逻辑提取, 仅 Anthropic provider 可用)
  └─ SubAgentSearchProvider      (Priority=999, 兜底, WebFetch + LLM 整理)
```

### 核心设计

1. **`ISearchProvider` 统一抽象** — 每个供应商实现 `SearchAsync(SearchRequest) → SearchProviderResult`，暴露 `Name`/`IsAvailable`/`Priority`。`IsAvailable` 检查 API key/配置是否就绪，未就绪的 provider 跳过不调用

2. **`SearchProviderCoordinator` 调度** — 注入 `IEnumerable<ISearchProvider>`，按 `Priority` 升序排列。遍历可用 provider 逐个尝试，第一个 `Success` 即返回。记录每个失败 provider 的错误，全失败时聚合到错误消息

3. **纵深兜底** — `SubAgentSearchProvider` 永远 `IsAvailable=true`（最后防线）。用 `IWebService.FetchAsync` 抓取 DuckDuckGo/Google 搜索结果页 HTML，再用 `IQueryService` 让 LLM 从 HTML 中提取结构化结果。无 API key 依赖，仅依赖 WebFetch + LLM

4. **配置驱动** — 各 provider 通过环境变量激活：`TAVILY_API_KEY`/`SERPER_API_KEY`/`BRAVE_API_KEY`/`SEARXNG_URL`。未配置的 provider `IsAvailable=false` 自动跳过

5. **现有 Anthropic 逻辑保留** — 提取为 `AnthropicSearchProvider`，仅当 `ProviderConfig.Definition.SupportsWebSearch=true` 时 `IsAvailable=true`。原有 `web_search_20250305` 服务端搜索链路不变

6. **`WebService.SearchAsync` 改造** — 删除 `SupportsWebSearch` 检查，改为委托 `SearchProviderCoordinator.SearchAsync`。输入验证（空 query、allowed+blocked 互斥）保留在 WebService 层

7. **DTO + JsonContext** — 各 provider 的请求/响应 DTO 用 `[JsonSerializable]` 源码生成上下文，NativeAOT 兼容，禁止反射

8. **复用 `IApiClient`** — 所有 HTTP 调用走现有 `IApiClient`，继承代理配置（`HTTPS_PROXY`）、SSRF 防护、重定向安全

### 目录结构

```
modules/cognition/hands/web/
  search_providers/
    contracts/
      ISearchProvider.cs          # 统一抽象
      SearchRequest.cs            # 统一请求 record
      SearchProviderResult.cs     # 统一结果 record
    coordinator/
      SearchProviderCoordinator.cs # 多供应商调度
      SearchProviderConfig.cs     # 配置聚合
    providers/
      TavilySearchProvider.cs
      SerperSearchProvider.cs
      BraveSearchProvider.cs
      SearXNGSearchProvider.cs
      AnthropicSearchProvider.cs
      SubAgentSearchProvider.cs
```

### 供应商 API 规范

| 供应商 | 端点 | 认证 | 返回结构 |
|--------|------|------|----------|
| Tavily | POST https://api.tavily.com/search | body.api_key | {answer, results:[{title,url,content,score}]} |
| Serper | POST https://google.serper.dev/search | header X-API-KEY | {organic:[{title,link,snippet}]} |
| Brave | GET https://api.search.brave.com/res/v1/web/search | header X-Subscription-Token | {web:{results:[{title,url,description}]}} |
| SearXNG | GET {SEARXNG_URL}/search?format=json | 无 | {results:[{title,url,content}]} |
| Anthropic | 现有 web_search_20250305 | x-api-key | 现有链路 |
| SubAgent | WebFetch DuckDuckGo + LLM 提取 | 无 | LLM 整理 |

## 替代方案

1. **单供应商（仅 Tavily）**：放弃。用户明确要求多供应商，且单点故障风险高（Tavily 宕核账号/额度耗尽即全挂）
2. **直接改 `SupportsWebSearch` 检查放宽**：放弃。治标不治本，非 Anthropic provider 仍无独立搜索能力，且违反"不依赖特定 provider"原则
3. **WebFetch + LLM 模拟作为主路径**：放弃。HTML 结构易碎、反爬虫、质量差，仅作兜底
4. **用 MediatR/策略模式而非 Coordinator**：放弃。过度设计，Coordinator 足够轻量，且项目已有 `MiddlewarePipeline` 模式，保持一致
5. **各 provider 独立 nuget 卫星包**：放弃。增加编译复杂度，provider 实现轻量（一个 HTTP 调用 + DTO），不值得拆包

## 后果

- **正向**：任何 provider 都能用 WebSearch；多供应商容错；兜底保证永不返回"不可用"
- **负向**：新增 6 个 provider 类 + 1 个 Coordinator，代码量增加；需配置多个 API key 才能用上对应供应商
- **中性**：现有 `WebServiceTests` 中 `SupportsWebSearch=false` 返回错误的测试需改为"所有 provider 不可用时走兜底"

## 验收

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| ISearchProvider + 6 个 provider + Coordinator | WebService.SearchAsync | ✅ | ✅ 编译0错误0警告 + 16单元测试通过 |
| TavilySearchProvider | SearchProviderCoordinator | ✅ | ✅ |
| SubAgentSearchProvider 兜底 | SearchProviderCoordinator | ✅ | ✅ |
| WebService.SearchAsync 改造 | WebToolHandlers.WebSearchAsync | ✅ | ✅ WebServiceTests 8通过 |
