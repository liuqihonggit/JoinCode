# ADR 0141: WebUI 桥接协议（.NET ⇄ Web 消息面）

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步更新此索引

> 状态: accepted（2026-10-18 落地验证：单测 23 绿 + 真机冒烟退出码 0）
> 创建: 2026-10-18
> 决策者: 用户主导（B2 路线内 AI 细化，ADR 0138 决策 3 的落地）
> 影响: modules/apps/gui/webview（宿主桥接层）+ webui 前端（js/bridge.js）；P6-1..P6-5 区域重构全部依赖本协议
> 关联: [ADR 0138](0138-gui-webview2-webui-rendering.md) · [ADR 0133](../state_machine_serialization/0133-json-dto-typed-conversion-ban-concat.md) · [TASK053](../../../../modules/apps/docs/task/TASK053-DSH像素对齐对比校验体系任务清单.md) P6-0b

## 背景

ADR 0138 决策 3：.NET 后端经桥接暴露 CLI 能力（斜杠命令、会话流、守卫确认），"桥接协议 spike 后细化"。spike（TASK054 S2）已实证 WebView2 postMessage 往返可用（378ms），但仅字符串 ping/pong，未定义消息面。

约束：

1. **NativeAOT**：禁反射序列化，必须 JsonContext 源生成（项目关键约束）
2. **JSON DTO 双向转换**（ADR 0133）：禁手拼 JSON 字符串
3. file:// 离线页：桥接为唯一 .NET↔Web 通道，无网络依赖
4. 后续 P6-1..P6-5 区域重构（侧栏/composer/消息区/设置/对话框）全部消费本协议，形状需类型安全、可扩展

## 决策

1. **传输**：WebView2 原生 postMessage 单通道。host→web 用 `CoreWebView2.PostWebMessageAsJson`；web→host 用 `window.chrome.webview.postMessage(obj)`
2. **消息形状**：类型化信封 = 判别字段 `"type"` + 类型内联字段（非嵌套 payload）。.NET 侧为 `abstract record BridgeMessage` 层级 + `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]`，经 `BridgeJsonContext : JsonSerializerContext` 源生成双向转换（AOT 安全，满足 ADR 0133）
3. **首版消息类型（最小集）**：
   - web→host：`web/ready`（握手）、`chat/send`（prompt）、`permission/response`（确认结果）、`smoke/pong`（冒烟）
   - host→web：`app/init`（主题/版本）、`chat/delta`（文本增量）、`chat/done`、`permission/request`（确认请求）、`smoke/ping`（冒烟）、`app/error`（错误指引）
4. **时序契约**：页面脚本执行 → web 发 `web/ready` → host 回 `app/init` → 此后双向事件流。host 收到未知/非法消息 → 日志（截断预览）+ 回 `app/error`（含诱导指引，符合错误提示必须解释"为什么/是谁触发/怎么做"），**不断链**
5. **冒烟契约**：`--webui-smoke` = 导航成功 → 等 `web/ready` → 发 `smoke/ping(nonce)` → 等 `smoke/pong(nonce)`，5s 看门狗。退出码：0 全通 / 2 导航失败 / 3 初始化失败 / 4 桥接超时或 nonce 不符
6. **前端薄层**：`webui/js/bridge.js` 暴露 `window.jccBridge = { send, on }`，零第三方依赖；业务消息注册集中在 bridge.js 尾部，与 .NET 侧 BridgeProtocol.cs 对照维护

## 替代方案

1. **JSON-RPC 2.0（method + params）** — 通用规范，但 params 弱类型（JsonElement 动态解析），与 ADR 0133"DTO 双向转换"冲突且 AOT 不友好；弃
2. **AddHostObjectToScript 直接暴露 .NET 对象给 JS** — 免序列化最省事，但 spike S2 实测 NativeAOT 运行期不可用（托管 COM 互操作 `NotSupportedException`）；弃
3. **每消息类型独立通道/多 host 对象** — WebView2 仅单 postMessage 通道；多通道无收益且丢统一审计面；弃

## 后果

### 正面
- AOT 安全（源生成）、类型双向（C# record ⇄ JS 字面量）、单通道统一日志/审计
- web/ready 握手天然解决时序竞态（host 订阅早于页面脚本，不会丢失就绪信号）
- 冒烟契约把"桥接可用"纳入 E2E（退出码 4），协议破坏早发现

### 负面
- 新增消息类型需双侧同步（BridgeProtocol.cs + bridge.js 注册）
- 前端引入第一段 JS（维护面出现，但保持薄层）

### 缓解
- 单测覆盖全类型 roundtrip + 判别字段断言，防双侧漂移
- 未知消息 fail-closed + app/error 诱导，AI/前端调试有明确路径

## 验证清单

- [x] 🔴 单元红：`BridgeProtocolTests`（全类型 roundtrip 等值 / 判别字段存在 / 未知 type 的 TryParse=false / 非法 JSON 不抛出）— 实测 CS0246 红 → 实现后 23 测试全绿
- [x] 🔴 E2E 红：`--webui-smoke` 在无 bridge.js 页面上桥接超时 → 退出码 4（实测 `BRIDGE-SMOKE timeout`）
- [x] 🟢 单元绿 + 真机冒烟退出码 0（实测日志 `BRIDGE web/ready` → `BRIDGE-SMOKE ok nonce=8d62c7ee454a469cbf77a675254df1bd`，握手+往返 ~100ms）
- [x] 全量 GUI 测试回归 591 通过 / 0 失败；webui 静态页像素校验无漂移（L3 基帧 meanDelta 1.7927 与基线一致 + 交互态 0 不一致 / 0 警告）
