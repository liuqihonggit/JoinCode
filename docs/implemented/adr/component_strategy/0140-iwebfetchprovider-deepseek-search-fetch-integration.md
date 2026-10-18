# ADR 0140: IWebFetchProvider 接口 + DeepSeek Search/Fetch API 集成

## 状态

accepted

## 上下文

DeepSeek 官方推出独立的 Search & Fetch API（2026-10）：

- **Search API**：`POST https://api.deepseek.com/v1/tools/search`，返回结构化搜索结果（title/url/snippet/summary/content/rerank_score/authority/date），自研搜索引擎，QPS=5，$0.75/1000 次
- **Fetch API**：`POST https://api.deepseek.com/v1/tools/fetch`，返回网页内容为 Markdown（title/content），QPS=5，$0.75/1000 次

工程已有 `ISearchProvider` 多供应商协调器模式（`SearchProviderCoordinator` 按 Priority 升序遍历 6 个搜索供应商：Tavily/Serper/Brave/SearXNG/Anthropic/SubAgent），但 **Fetch 仅有中间件管道模式**（`MiddlewarePipeline<WebContext>` 7 个中间件：Validation→SsrfGuard→CacheCheck→DomainCheck→Fetch→ContentProcessing→CacheWrite），无多供应商抽象。

**问题**：DeepSeek Fetch API 作为外部 API 调用，与管道中直接 HTTP 抓取 + HTML 转 Markdown 的模式不同。需要决定如何集成。

## 决策

采用 **IWebFetchProvider 接口 + WebService.FetchAsync 优先尝试 fetch provider 回退管道** 方案：

1. **`IWebFetchProvider` 接口**（`modules/cognition/hands/web/fetch_providers/contracts/`）— 类比 `ISearchProvider`，定义 `Name` / `IsAvailable` / `FetchAsync`
2. **`DeepSeekSearchProvider`**（Priority=90）实现 `ISearchProvider` — Bearer 认证，Snippet 优先取 `summary`（600-800 字符适合模型）回退 `snippet`（100-200 字符适合 UI），解析 `date` 为 `PublishedDate`
3. **`DeepSeekFetchProvider`** 实现 `IWebFetchProvider` — Bearer 认证，返回 Markdown，title 非空时前置 `# title` 标题
4. **`WebService.FetchAsync` 改造** — 优先遍历 `IWebFetchProvider` 供应商，第一个成功即返回；全失败回退到中间件管道（保持向后兼容）
5. **`ProviderCategory` 枚举新增 `Fetch` 值** + `FetchProviderDescriptor` 适配器 — fetch provider 通过 `ProviderCatalog` 统一可见

**共享 API Key**：Search 和 Fetch 复用同一 `DEEPSEEK_API_KEY` 环境变量，通过 `SearchProviderConfigHelper.ResolveEnvVar(config, "deepseek", "apiKeyEnvVar", "DEEPSEEK_API_KEY")` 支持配置覆盖。

## 替代方案（考虑过但放弃）

| 方案 | 优点 | 缺点 | 放弃原因 |
|------|------|------|---------|
| B: 中间件管道插入 DeepSeekFetchMiddleware | 非侵入式，复用管道 | 需理解管道排序机制（DI 注入顺序无显式 Order）；短路后跳过 CacheWrite；中间件间状态传递复杂 | 管道为线性 HTTP 抓取设计，外部 API 调用语义不符 |
| C: WebService 直接依赖 DeepSeekFetchProvider 硬编码 | 最简单 | 耦合具体类，不可扩展，违反"万物皆插件" | 未来加 Tavily Fetch 需改 WebService |
| D: 不集成 Fetch API，仅集成 Search | 最小变更 | DeepSeek Fetch 返回高质量 Markdown，比自研 HTML 转换更好；用户明确要求全部做 | 功能缺失 |

## 后果

**正面**：
- Fetch provider 可扩展（未来加 Tavily Fetch 等只需实现 `IWebFetchProvider`）
- Search 和 Fetch 对称设计（`ISearchProvider` + `IWebFetchProvider`），降低认知负担
- 向后兼容：无 fetch provider 时回退到原有管道，现有测试不破坏
- `ProviderCatalog` 统一展示 LLM/Search/Fetch/Embedding 四类供应商

**负面**：
- `WebService` 构造函数新增参数（`IEnumerable<IWebFetchProvider>`），但 DI 自动注入空集合，不破坏现有注册
- fetch provider 短路时跳过管道的 CacheWrite（DeepSeek Fetch 结果不缓存），如需缓存可在 provider 内部实现

**验证**：编译 0 警告 0 错误，24 个 DeepSeek 单元测试 + 80 个 web 测试全通过。

## 实现

| 组件 | 路径 |
|------|------|
| `IWebFetchProvider` 接口 | `modules/cognition/hands/web/fetch_providers/contracts/IWebFetchProvider.cs` |
| `DeepSeekSearchProvider` | `modules/cognition/hands/web/search_providers/providers/deepseek/DeepSeekSearchProvider.cs` |
| `DeepSeekFetchProvider` | `modules/cognition/hands/web/fetch_providers/providers/deepseek/DeepSeekFetchProvider.cs` |
| `FetchProviderDescriptor` | `modules/cognition/hands/web/provider_catalog/FetchProviderDescriptor.cs` |
| `WebService` 改造 | `modules/cognition/hands/web/services/WebService.cs` |
| `ProviderCategory` 枚举 | `modules/abstractions/abstractions/abs_core/configuration/providers/ProviderCategory.cs` |
| 单元测试 | `modules/cognition/hands.api.tests/web/SearchProvidersTests.cs` + `DeepSeekFetchProviderTests.cs` |
