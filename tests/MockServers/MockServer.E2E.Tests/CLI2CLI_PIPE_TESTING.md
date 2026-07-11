# JoinCode.Tests 管道测试文档

## 概述

JoinCode.Tests 管道测试是一种端到端测试架构，通过 C# 命名管道（Named Pipe）实现 `MockServer.E2E.Tests` 测试项目与 `JoinCode` 之间的通信。

### 核心概念

- **Mock Server**: 测试项目冒充 OpenAI 服务器，通过管道接收 HTTP 请求字符串
- **JoinCode 客户端**: JoinCode 项目通过管道发送 HTTP 请求到 Mock Server
- **验证**: 测试项目验证 JoinCode 发送的请求是否包含正确的系统提示词和用户提示词

## 架构

```
┌─────────────────────────────────────────────────────────────────┐
│           MockServer.E2E.Tests (Mock Server)           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              PipeOpenAIMockServer                       │   │
│  │  - NamedPipeServerStream (双向通信)                     │   │
│  │  - 解析 HTTP 请求字符串                                 │   │
│  │  - 返回 HTTP 响应字符串                                 │   │
│  └─────────────────────────────────────────────────────────┘   │
│                          ▲                                      │
│                          │ Named Pipe                           │
│                          ▼                                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              JoinCode (客户端)                    │   │
│  │  - PipeHttpClient                                       │   │
│  │  - PipeChatCompletionService                            │   │
│  │  - 发送 HTTP 请求字符串                                 │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## 文件结构

### Mock Server 核心文件

```
tests/MockServer.E2E.Tests/Core/
├── PipeOpenAIMockServer.cs      # 管道 Mock Server 实现
├── MockServerOptions.cs         # Mock Server 配置选项
├── HttpRequestParser.cs         # HTTP 请求解析器
├── HttpResponseBuilder.cs       # HTTP 响应构建器
├── OpenAIRequestModels.cs       # OpenAI 请求模型
├── OpenAIResponseModels.cs      # OpenAI 响应模型
├── RequestRecorder.cs           # 请求记录器
└── PromptValidator.cs           # 提示词验证器
```

### 测试触发器系统

```
tests/MockServer.E2E.Tests/Triggers/
├── TestTriggerTypes.cs          # 测试触发器类型枚举
├── TestTriggerParser.cs         # 测试触发器解析器
├── MockResponseGenerator.cs     # Mock 响应生成器
├── SystemPromptValidator.cs     # 系统提示词验证器
├── UserPromptInjectionValidator.cs  # 用户提示词注入验证器
└── ToolPromptValidator.cs       # 工具提示词验证器
```

### 测试框架集成

```
tests/MockServer.E2E.Tests/Fixtures/
├── PipeMockServerFixture.cs     # xUnit Fixture
├── PipeTestCollection.cs        # 测试集合定义
├── PipeJoinCodeProcessFactory.cs     # JoinCode 进程工厂
└── OpenAIMockTestBase.cs        # 测试基类
```

### 端到端测试

```
tests/MockServer.E2E.Tests/E2E/
├── SystemPromptPipeE2ETests.cs          # 系统提示词验证测试
├── UserPromptInjectionPipeE2ETests.cs   # 用户提示词注入测试
├── ChatFlowPipeE2ETests.cs              # 完整聊天流程测试
└── ApiKeyValidationE2ETests.cs          # API Key 验证测试
```

### JoinCode 管道客户端

```
src/JoinCode/Pipe/
├── PipeHttpClient.cs            # 管道 HTTP 客户端
├── PipeHttpMessageHandler.cs    # 管道 HTTP 消息处理器
├── PipeConnectionOptions.cs     # 管道连接选项
└── HttpRequestSerializer.cs     # HTTP 请求序列化器
```

## 使用方法

### 运行测试

```powershell
# 运行所有管道测试
dotnet test tests/MockServer.E2E.Tests/MockServer.E2E.Tests.csproj

# 运行特定测试类
dotnet test tests/MockServer.E2E.Tests/MockServer.E2E.Tests.csproj --filter "SystemPromptPipeE2ETests"

# 详细输出
dotnet test tests/MockServer.E2E.Tests/MockServer.E2E.Tests.csproj --verbosity detailed
```

### 测试触发器

在测试中可以使用 `[TEST:...]` 格式的触发器来验证特定功能：

```csharp
// 验证系统提示词
[TEST:SYSTEM_PROMPT]

// 验证用户提示词
[TEST:USER_PROMPT]

// 验证工具提示词
[TEST:TOOL_PROMPT:agent]

// 验证用户提示词注入
[TEST:USER_INJECTION]

// 返回完整请求
[TEST:FULL_REQUEST]
```

### 编写新测试

```csharp
[Collection(nameof(PipeTestCollection))]
public class MyPipeE2ETests : OpenAIMockTestBase
{
    public MyPipeE2ETests(PipeMockServerFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task MyTest_ShouldWork()
    {
        // 启动 JoinCode
        await StartCliAsync();

        // 发送用户输入
        await SendInputAsync("[TEST:SYSTEM_PROMPT]");

        // 等待响应
        var response = await WaitForResponseAsync();

        // 验证请求
        var lastRequest = Fixture.RequestRecorder.GetLastRequest();
        lastRequest.Should().NotBeNull();

        // 验证系统提示词
        var systemPrompts = lastRequest!.GetSystemPrompts();
        systemPrompts.Should().Contain(s => s.Contains("预期内容"));
    }
}
```

## API Key 格式

测试使用模拟的 OpenAI API Key：

```
Authorization: Bearer sk-test-1234567890abcdef
```

格式要求：
- 必须以 `Bearer ` 开头
- Token 必须以 `sk-` 开头
- 支持 `sk-test-*` 和 `sk-proj-*` 格式

## HTTP 请求/响应格式

### 请求格式

```http
POST /v1/chat/completions HTTP/1.1
Host: api.openai.com
Authorization: Bearer sk-test-1234567890abcdef
Content-Type: application/json
Content-Length: 1234

{
  "model": "gpt-4o",
  "messages": [
    {"role": "system", "content": "系统提示词..."},
    {"role": "user", "content": "用户输入..."}
  ],
  "stream": true
}
```

### 响应格式（非流式）

```http
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 567

{
  "id": "chatcmpl-test-123",
  "object": "chat.completion",
  "created": 1234567890,
  "model": "gpt-4o",
  "choices": [{
    "index": 0,
    "message": {
      "role": "assistant",
      "content": "助手响应..."
    },
    "finish_reason": "stop"
  }]
}
```

### 响应格式（流式 SSE）

```http
HTTP/1.1 200 OK
Content-Type: text/event-stream
Transfer-Encoding: chunked

data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"delta":{"role":"assistant"}}]}

data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"delta":{"content":"Hello"}}]}

data: [DONE]
```

## 测试覆盖

### 系统提示词验证 (SystemPromptPipeE2ETests)

- ✅ 系统提示词正确发送
- ✅ 多个系统提示词消息处理
- ✅ 系统提示词内容验证
- ✅ 系统提示词模式匹配
- ✅ 空系统提示词处理
- ✅ 长系统提示词记录

### 用户提示词注入验证 (UserPromptInjectionPipeE2ETests)

- ✅ 系统提示词覆盖类型检测
- ✅ 角色扮演类型检测
- ✅ 指令忽略类型检测
- ✅ 分隔符类型检测
- ✅ 负面关键词触发适应性提示词
- ✅ 继续关键词触发上下文延续
- ✅ 正常输入不误判

### 聊天流程验证 (ChatFlowPipeE2ETests)

- ✅ 单轮对话流程
- ✅ 多轮对话流程
- ✅ 系统提示词一致性
- ✅ 用户消息传递
- ✅ 流式对话请求
- ✅ 带参数聊天请求
- ✅ 长对话历史记录

### API Key 验证 (ApiKeyValidationE2ETests)

- ✅ 有效 API Key 格式
- ✅ Bearer Token 格式
- ✅ sk- 前缀验证
- ✅ 无效 API Key 处理
- ✅ 空 API Key 处理

## 故障排除

### 管道连接失败

确保：
1. 管道名称格式正确：`JoinCode_MockServer_{Guid}`
2. 服务器先于客户端启动
3. 使用 `NamedPipeServerStream` 而非 `AnonymousPipeServerStream`

### 请求解析失败

检查：
1. HTTP 请求格式正确
2. Content-Length 与实际内容长度匹配
3. JSON 格式有效

### 测试超时

可能原因：
1. JoinCode 进程未正确启动
2. 管道连接未建立
3. 请求/响应循环阻塞

## 注意事项

1. **管道名称唯一性**: 每个测试使用唯一的管道名称，避免冲突
2. **资源清理**: 测试完成后确保停止 JoinCode 进程和 Mock Server
3. **并发测试**: 当前实现支持并行测试，每个测试使用独立的管道
4. **平台限制**: Named Pipe 在 Windows 上完全支持，在 Linux/macOS 上可能需要调整

## 相关文件

- 原始 Moq 测试已移动到 `.x/ProgramTests.cs.del`
- 项目文件已更新排除 `.x/` 目录
