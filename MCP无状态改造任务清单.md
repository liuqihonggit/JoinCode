# MCP 无状态改造任务清单

## 背景
最新 MCP 规范 2025-11-25 的 Streamable HTTP transport 中,服务器**可选**分配 `MCP-Session-Id`:
- 分配 = 有状态(服务器维护会话)
- 不分配 = 无状态(每个请求自包含,可水平扩展/serverless 友好)

项目当前协议版本停留在 2024-11-05(初版),且 HttpTransport 实现已是 Streamable 风格但版本号标错。

## 决策依据
- **一刀切到最新**:不保留旧协议兼容,只支持 2025-11-25(项目配置无 sse 用户,无实际依赖)
- **归档旧类不删除**:守 AGENTS.md 红线,旧 Sse 类移到 `.xxx/`,从工厂/加载器/枚举移除引用
- **不搞基类继承/自动探测**:旧协议直接废弃,无需 HttpTransportBase/探测器
- **切换协议最方便**:全量 2025-11-25,无需切换;TransportType.Sse 配了报废弃错误

## 任务列表

### P0 协议版本对齐 [已完成 ✅]
- [x] P0.1 `McpProtocolVersion` 常量升级 2024-11-05 → 2025-11-25 + 历史版本 + Supported 集合
- [x] P0.2 `McpClientOptions` / `McpClientOptionsBuilder` 默认值同步
- [x] P0.3 `HttpTransport` 发送 `MCP-Protocol-Version` 头部 + `HttpTransportOptions.ProtocolVersion` 字段
- [x] P0.4 握手版本协商:服务器返回版本不支持则抛 McpProtocolException
- [x] P0.5 测试:HttpTransportOptionsTests 6 个测试全绿,全量 151 测试全绿,0 破坏

### P4 归档旧协议 [已完成 ✅]
- [x] P4.1 SseClientTransport/SseTransport/McpSseClient 移到 `services/Mcp/.xxx/`(加 .del 后缀)
- [x] P4.2 从 McpClientFactory/McpbLoader/McpClientToolHandlers 移除 Sse 引用(8 处)
- [x] P4.3 McpClientTransportType.Sse 枚举值移除(生成器全量重建)
- [x] P4.4 McpClientOptionsBuilder.UseSse() 移除
- [x] P4.5 McpbLoader "sse" 自动迁移到 Http(无缝过渡)
- [x] P4.6 全链路编译通过(Foundation→Mcp→Composition),151 测试全绿

### P1 Streamable HTTP 客户端补全 [已完成 ✅]
- [x] P1.1 HTTP DELETE 终止会话(StopAsync 时发送 DELETE + MCP-Session-Id)
- [x] P1.2 GET + `Last-Event-ID` 重连(OpenSseStreamAsync + _lastEventId 记录)
- [x] P1.3 404 SessionExpired 事件(上层可监听重新握手)
- [x] P1.4 151 测试全绿,0 破坏

### P2 无状态模式显式化
- [ ] P2.1 `HttpTransportOptions.StatelessMode` 开关
- [ ] P2.2 握手后自动检测服务器未返回 Session-Id → 标记无状态

### P3 服务端 Streamable HTTP
- [ ] P3.1 新增 `McpHttpServer`(Kestrel)单端点 POST/GET
- [ ] P3.2 无状态模式(不分配 session)
- [ ] P3.3 有状态模式(分配 session + SSE 推送 + session 存储)
- [ ] P3.4 Origin 校验 + 安全合规

### P5 E2E + 文档
- [ ] P5.1 MockServer 升级到 2025-11-25
- [ ] P5.2 无状态/有状态双场景 E2E
- [ ] P5.3 更新 AGENTS.md MCP 协议版本

## 执行原则
- 渐进式:每步 红测试 → 改 → 编译 → 绿测试 → commit
- 守红线:不删文件,归档到 `.xxx/{名}.{后缀}.{时间戳}.del`
- TDD:P0.3/P0.4/P1/P2/P3 有逻辑的写 TDD;P0.1/P0.2 纯常量无需 TDD

<!-- 🤖 Auto Decision: 2026-08-21 -->
<!-- 决策: 一刀切到 2025-11-25,不保留旧协议兼容 -->
<!-- 原因: 项目配置无 sse 用户,旧协议无实际依赖;保留旧协议需基类继承+自动探测器,复杂度高收益低 -->
<!-- 替代方案: 双协议共存+自动探测(规范 Backwards Compatibility),但工作量大且本项目无旧server需求 -->
<!-- 验证: P0 编译通过,151 测试全绿 ✅ -->
