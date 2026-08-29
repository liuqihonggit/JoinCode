# 0044. 错误码统一规范 — [PREFIX+数字] 格式

- 状态：proposed
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目中异常消息携带错误码用于快速定位问题源。现有错误码前缀分散，未明确分类边界：

| 前缀 | 含义 | 数量 | 示例 |
|------|------|------|------|
| GRD | Guard 模块 | 16 | [GRD016] 模型未注册 |
| SSE | SSE 传输 | 4 | [SSE001] 传输未就绪 |
| GEN | 通用/测试 | 66 | [GEN036] 不变量违反 |
| E2E | E2E 测试 | 14 | [E2E001] 获取请求超时 |

问题：
1. **GEN 范围过宽**：66 个错误码覆盖文件系统、进程管理、测试框架等不同领域，难以按前缀定位
2. **前缀分类未文档化**：新增错误码时无明确归属规则，开发者随意选前缀
3. **错误码集中度不足**：部分错误码内联在异常消息中，未集中到 ErrorMessages 常量类

## 决策

### 1. 前缀分类规范化

| 前缀 | 模块/领域 | 编号范围 | 说明 |
|------|-----------|----------|------|
| GRD | core/safety/Guard | 001-999 | 安全守卫、权限、沙箱、配置 |
| LLM | core/ai/Llm | 001-999 | LLM 适配器、查询服务 |
| AGT | core/ai/Agents | 001-999 | Agent 协调、Spawn、Doctor |
| BRN | core/execution/Brain | 001-999 | 上下文、Prompt、CostTracking |
| HND | core/execution/Hands | 001-999 | 工具处理器、Shell、Desktop |
| SCH | core/execution/Scheduling | 001-999 | 任务调度、Cron、Workflow |
| MCP | services/Mcp | 001-999 | MCP 协议、远程同步 |
| BRG | services/Bridge | 001-999 | Bridge 会话、消息 |
| INF | infrastructure | 001-999 | 基础设施、IO、Transport |
| CMP | composition | 001-999 | 组合层、Clock |
| GEN | 通用/跨模块 | 001-999 | 文件系统、进程管理、通用工具 |
| E2E | E2E 测试 | 001-999 | 端到端测试专用 |
| JCC | 分析器/生成器 | 001-999 | 编译期分析器规则（JCC5002/JCC9006 等） |

### 2. 错误码集中管理

生产代码错误码集中到 `ErrorMessages.cs` 常量类，禁止内联字符串：
```csharp
// ✅ 正确
throw new ConfigurationException(ErrorMessages.ModelNotRegistered(modelId, profile));

// ❌ 禁止
throw new ConfigurationException($"[GRD016] 模型 '{modelId}' 未注册...");
```

### 3. 格式统一

`[PREFIX+零填充三位数字] 描述`，如 `[GRD016]`、`[LLM003]`。三位数字足够覆盖单模块 999 个错误码。

## 替代方案

1. **全局连续编号**：放弃。无法按前缀定位模块，编号冲突难管理。
2. **不用错误码**：放弃。异常消息可变，错误码是稳定标识，用于日志搜索和文档引用。
3. **每个类独立前缀**：放弃。前缀过多（上百个类），认知成本高。

## 后果

- 正面：按前缀定位模块；错误码集中管理易审计；新增错误码,有明确归属
- 负面：现有 GEN 错误码需拆分到各模块前缀，改动面大；内联错误码需提取到常量类
- 中性：渐进式迁移，每次修改一个模块时同步迁移错误码
