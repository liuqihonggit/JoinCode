# DSG043 — TUI Markdown 链接渲染 OSC 8 深链路（T4 方案 B 落地设计）

> 状态：proposed（2026-10-23）
> 关联：TASK065 第七波 T4（HyperlinkEmitter/IssueReferenceExpander 纯数据 API 已合入）、ADR 0140（TUI 自渲染像素对齐）、DSG042（输入泵 — kitty 键盘协议同一原始流前置）
> 前置调研：T4 架构建议（A/B/C 方案对比，推荐 B）+ S12 最小面评估（5 层改造清单）

## 1. 问题

Claude Code 基准的 Markdown 渲染含终端超链接（utils/markdown.ts:141-161 createHyperlink OSC 8 包裹 + :289-308 issue 引用链接化）。本项目 T4 数据层已就绪（Wrap/IsHyperlinkSupported/Expand 102 测试全绿），但渲染链未消费——链接在 TUI 里是纯文本。

**S12 实证的五层断点**：

| # | 断点 | 位置 | 性质 |
|---|------|------|------|
| ① | Link 标签零产出 | InlineMarkdownParser（`[text](url)` 语法未解析——段全部 plain/emphasis） | 解析层 |
| ② | 无 Url 载体 | MarkdownSegment（Style 枚举 + Text，无 Url 字段） | 数据模型 |
| ③ | bridge 契约 | MarkdownDocument 跨进程 JSON 多态（engine 侧镜像 DTO）——加字段即契约变更 | 协议层 |
| ④ | 折行区间拆分 | MarkdownLayout 折行后链接区间跨行需拆分（列偏移换算） | 布局层 |
| ⑤ | 差分序列配对 | Screen/FrameComposer Cell 网格差分 vs OSC 8 开/闭流式序列对 | 渲染层 |

## 2. 方案 B — 行文本级区间注入（T4 推荐，本 DSG 细化）

**核心思想**：Cell 网格保持零链接感知（⑤ 绕开）；链接在**行输出层**以区间表注入——与基准 ink 同构（"文本流区间标记"在网格架构上的最小映射）。

### 数据链（自底向上）

```
① 解析: InlineMarkdownParser 增 [text](url) → MarkdownSegment(Link text + Style.Link)
② 模型: MarkdownSegment 增 Url string?（null=非链接段）— AOT DTO 末尾可选参数先例
        （SessionInfo.Vendor 同款，旧 JSON 零破坏）
③ 协议: bridge JSON 镜像同步（engine 侧 MarkdownSegment DTO 同字段；WhenWritingNull）
④ 布局: MarkdownLayout 折行时链接段产出 (行, 列区间, url) 三元组表 → 行对象携带
        LineLinks: (StartCol, EndCol, Url)[]（折行拆分：链接跨行时每行区间独立）
⑤ 渲染: InlineComposer/FrameComposer 行输出 — 拼行文本时按区间表调
        HyperlinkEmitter.Wrap(切片, url) + IsHyperlinkSupported() 降级纯文本；
        行缓存缓存"预注入 ANSI 的成品行"（链接变化=行内容变化，diff 语义不变）
```

### 关键设计点

1. **渲染注入位置**：`InlineComposer`（append 滚入 scrollback 路径）逐行写终端处——cell 网格→文本行转换后的最后一站；viewport 全屏路径（FrameComposer diff 输出）同理。两处共享 `AnsiLineComposer`（新静态类：行文本+区间表+样式段 → ANSI 成品行）
2. **样式段与链接区间正交**：现有 SGR 样式段拼装照旧；链接区间在**最终文本流**上再包裹（OSC 8 与 SGR 是终端流上两条正交标记——基准 ink 同构）
3. **剪贴板/复制面**：CopyTextExtractor/TranscriptLines 已走纯文本源（item 数据层），不受 ANSI 注入影响
4. **diff 语义**：成品 ANSI 行字节即缓存键——链接区间变化必然导致行字节变化，既有 CellDiffer 无需感知
5. **降级链**：`IsHyperlinkSupported()` false → Wrap 直通纯文本（终端白名单探测 T4 已就绪）；mailto → 纯文本 email（基准同语义）
6. **issue 引用**：`IssueReferenceExpander.Expand(text, defaultOwnerRepo)` 在 ① 解析后追加 pass（owner/repo 从 git remote 解析——Host 启动时注入 defaultOwnerRepo 构造参数）

### 分批（三 Phase）

| Phase | 内容 | 验收 |
|-------|------|------|
| **P1 解析+模型+协议** | ①②③：`[text](url)` 解析 + MarkdownSegment.Url + bridge 镜像 | JSON 契约测试（新字段 WhenWritingNull 旧快照零破坏）+ 解析单测 |
| **P2 布局区间表** | ④：折行拆分 + LineLinks 行对象携带 | 布局单测（链接跨行区间正确/单行区间/无链接空表） |
| **P3 渲染注入** | ⑤：AnsiLineComposer + 两 Composer 接线 + 探测降级 + issue 引用 pass | exe 手动验收（Windows Terminal 链接可点击）+ golden 行字节测试 |

## 3. 替代方案（T4 已论证，存档）

- **A Cell 元数据段**（linkId+LinkPool）：网格级一体但改 Cell/Screen/FrameComposer 三核心+差分复杂化——演进期方案，区间数据源与 B 同构可平移升级
- **C StylePool 扩展**：语义污染+组合爆炸，排除

## 4. 风险

- Windows conhost（非 WT）OSC 8 不支持 → 探测降级路径必须 P3 验收覆盖
- bridge 跨进程旧引擎（无 Url 字段 JSON）→ ②③ 可选参数容忍，TUI 侧 Url=null 走纯文本
- 行缓存内存（成品 ANSI 行含转义序列字节更长）→ 现有行缓存容量评估，涨幅预期 <15%

## 5. 未决（实施时裁决）

1. issue 引用 Expand 的 pass 位置：解析器内 vs MarkdownMessageItem 构造时（倾向后者——解析器保持语法单一职责）
2. 链接视觉样式（基准蓝色）：Style.Link 映射 theme 哪个色键（建议复用 Suggestion 或新增 Link 键——theme.ts 基准对照）
3. DSG042 kitty 协议落地后 raw-input 通道的 OSC 8 回写响应是否需要消费（终端能力协商升级路径）
