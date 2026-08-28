# 0031. HTTP 连接池 DNS 刷新

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

`SocketsHttpHandler` 默认 `PooledConnectionLifetime=Infinity`，意味着连接池中的连接永不过期。如果 DNS 记录变更（如 API 端点 IP 变化），旧连接仍会用旧 IP，导致请求失败。

## 决策

**显式设置 `PooledConnectionLifetime`，确保 DNS 变更后连接刷新。**

定位文件：`core/ai/Llm/src/Adapters/LLM/QueryServiceBase.cs:70`

```csharp
// 决策: SocketsHttpHandler 默认 PooledConnectionLifetime=Infinity，DNS 变更不刷新
```

具体设置（按供应商）：
- 一般 API：`PooledConnectionLifetime = TimeSpan.FromMinutes(2)`
- 长连接 API：可适当延长，但不设 Infinity

## 替代方案

1. **保持默认 Infinity**：放弃。DNS 变更后旧连接失败，需重启进程才恢复。
2. **每次创建新 HttpClient（不池化）**：放弃。性能差，TLS 握手开销大。
3. **用 IHttpClientFactory 自动管理**：部分采用。`IHttpClientFactory` 内部管理 `HttpMessageHandler` 生命周期，但需配置 `HandlerLifetime`。

## 后果

- 正面：DNS 变更后自动恢复；连接池复用性能好
- 负面：连接定期重建，有少量握手开销
- 中性：`PooledConnectionLifetime` 按供应商配置，可调整
