# ADR 0102: 文件夹整理改革 — 从领域驱动到功能驱动

> 状态: proposed
> 创建: 2026-09-12
> 决策者: 用户主导
> 影响: 全量迁移，废弃七层架构和脑/眼/手分类

## 背景

现有项目按"脑/眼/手"领域隐喻组织：

```
foundation/Abstractions/  → 00-core ~ 09-composition (10个领域编号)
core/execution/           → Brain / Hands / McpToolDispatch / Scheduling
services/                 → Bridge / Dream / Eyes / Mcp / Vision ...
composition/Commands/     → agents / ai / brain / guard / hands / system / transport
```

## 问题

1. **工具分散3处共73个McpTool文件**：Hands(27) + McpToolDispatch(10) + services/Mcp(36)，找一个 editor 工具需在3处搜索
2. **FileToolHandlers.cs 1972行**含 read/write/edit/delete/list，单文件过大
3. **提示词分散2处**：foundation/Abstractions/01-ai/Prompts 和 core/execution/Brain/src/Prompts
4. **"脑/眼/手"是隐喻非功能**：同一功能（如 read）被拆到多领域（handler 在 Hands，prompt 在 01-ai）
5. **违反约定**：多处文件夹/文件混排、00-core/Models 34子目录超30上限、深度7层

## 决策

### 决策1：全量迁移到功能驱动分层

废弃"脑/眼/手"领域分类，改为按功能分类：

```
root/
├── 00_generators/       # 源码生成器（编译最底层）
├── 01_shared/           # 全局共享抽象（原 Abstractions 核心）
├── 02_llm/              # LLM 抽象与实现
├── 03_builtin/          # 内置工具（read/edit/bash/git/search...）
├── 04_mcp/              # MCP 工具（code_analysis/github/skill...）
├── 05_server/           # 服务（bridge/vision/codeindex/sandbox...）
├── 06_prompts/          # 提示词（与工具分离，按 builtin/mcp/server 分）
├── 07_slash/            # 斜杠命令
├── 08_composition/      # 组合层
├── 09_app/              # 应用层（cli/gui/tui/sdk）
└── tests/
```

### 决策2：废弃七层编译依赖链

原七层：generators → foundation → infrastructure → core → services → composition → app
新九层：00_generators → 01_shared → 02_llm → 03_builtin → 04_mcp → 05_server → 06_prompts → 07_slash → 08_composition → 09_app

编译依赖链按编号顺序，每层只能引用编号更小的层。

### 决策3：csproj 按功能分组

每个功能层一个主 csproj（如 `builtin.csproj`），包含该层所有工具。每个工具是子目录，含 `private/`（私人助手）。本层有 `shared/`（共享助手）。

```
03_builtin/
├── builtin.csproj
├── shared/                    # 本层共享
├── read/
│   ├── ReadHandler.cs
│   └── private/               # read 专用助手
├── edit/
│   ├── EditHandler.cs
│   └── private/
└── bash/
    ├── BashHandler.cs
    └── private/
```

### 决策4：命名规范

- 文件夹用**下划线**，禁止连字符（如 `builtin_read` 非 `builtin-read`）
- 功能前缀 + 工具名（如 `mcp_github`、`server_bridge`）
- 编号前缀 00-09 保证编译顺序

### 决策5：private/shared 语义

- `private/`：该工具专用的辅助类、扩展、内部服务，不对外暴露
- `shared/`：本层所有工具共享的辅助类、基类、接口

## 替代方案（已放弃）

1. **仅改工具组织（小改）**：改动小但"脑/眼/手"根因未除，未来仍难找工具
2. **保留七层外壳**：安全但领域分类仍在，功能分散问题未解决
3. **每工具一csproj**：隔离最好但编译慢、csproj爆炸

## 影响

### 正面
- 工具按功能聚合，找 read/edit/bash 只看 `03_builtin/`
- 提示词与工具分离但同功能对应（`06_prompts/builtin/read/` 对应 `03_builtin/read/`）
- 扁平化 + 前缀排序，项目地图清晰
- private/shared 明确暴露边界，降低"某服务是全局还是单例"的疑惑

### 负面
- 全量迁移，改动量大
- 所有 ProjectReference 路需更新
- GlobalUsings.cs 命名空间需同步
- Stryker.slnx / CI matrix 需同步
- 源码生成器输出路径需验证

### 缓解
- 渐进式：从 00_generators 开始逐层往上
- 每阶段编译 + 测试 + 提交
- 每层一个 README.md 说明本层和下方文件类型

## 验证清单

- [ ] 全量编译通过
- [ ] 全量测试通过
- [ ] 所有 README.md 更新
- [ ] Stryker.slnx 更新
- [ ] CI matrix 更新
- [ ] 约定满足：≤30子文件夹、不混排、≤3层深度、下划线命名
