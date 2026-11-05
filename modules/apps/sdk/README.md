# JoinCode SDK

一行代码启动 AI Coding 助手。

## 安装

```bash
dotnet add package JoinCode.Sdk
```

## 使用

```csharp
using JoinCode.Sdk;

var builder = Host.CreateApplicationBuilder(args);

builder.Host.AddJoinCode(options => {
    options.Vendor = VendorKind.ZhiPu;
    options.ModelId = "glm-5.2";
    options.ApiKey = Environment.GetEnvironmentVariable("ZHIPU_API_KEY");
    options.Language = "zh";
});

var host = builder.Build();
await host.RunAsync();
```

## 配置选项

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Vendor` | `VendorKind` | `OpenAi` | LLM 供应商 |
| `ModelId` | `string` | `""` | 模型 ID |
| `ApiKey` | `string?` | `null` | API Key（也可通过环境变量提供） |
| `BaseUrl` | `string?` | `null` | API 基础 URL（使用默认端点时留空） |
| `Language` | `string` | `"zh"` | 界面语言代码 |

## 细粒度引用

如果只需要部分组件，可以单独引用：

```bash
dotnet add package JoinCode.Abstractions      # 接口契约
dotnet add package JoinCode.Abstractions.AI    # AI 抽象
dotnet add package JoinCode.Llm               # LLM 引擎
dotnet add package JoinCode.Brain             # 上下文管理
dotnet add package JoinCode.Hands             # 工具执行
dotnet add package JoinCode.Guard             # 安全守卫
dotnet add package JoinCode.Mcp               # MCP 协议
```

## 架构

```
JoinCode.Sdk (聚合包)
├── JoinCode.Abstractions.*    (接口契约层)
├── JoinCode.Llm               (LLM 引擎)
├── JoinCode.Brain             (上下文 + 查询引擎)
├── JoinCode.Hands             (工具处理器 + 技能系统)
├── JoinCode.Guard             (安全守卫)
├── JoinCode.Mcp               (MCP 协议)
├── JoinCode.Composition       (组合编排)
└── JoinCode.Infrastructure    (基础设施)
```

## NativeAOT

JoinCode SDK 完全兼容 NativeAOT，Release 模式自动启用 `PublishAot` + `TrimMode=full`。
