# 多模态隐喻显露工具 PRD

> 状态：规划中（待确认思维缺陷） ｜ 创建：2026-08-28 ｜ 负责：待定

## 1. 背景与目标

### 1.1 问题陈述
LLM 拥有多模态隐空间（视觉/时序），但 Agent 层缺乏"手"——无法在图片上**标注、缩放、测量、递归深挖**。现有 `MultimodalUiElementDetector` 只能一次性把整张截图喂给 LLM 得到 UI 元素列表，存在三个缺陷：

1. **无缩放递归**：看不清就无能为力，无法局部放大二次识别
2. **无结构化锚点**：LLM 输出坐标是浮点猜测，无稳定编码可引用、可复现、可叠加
3. **无时序纠偏**：单帧易被嵌入文字误导，缺乏多帧交叉验证

### 1.2 目标
提供一套**朴素、抗幻觉、毫秒级**的图片标注与深挖工具集，让 LLM 通过结构化 JSON 驱动四叉树网格对图片进行**染色标注、递归缩放、时序聚合、尺寸测量**，把"看不清"变成"放大再看"，把"单帧幻觉"变成"多帧佐证"。

### 1.3 设计原则
| 原则 | 说明 |
|------|------|
| 识图不生图 | 只用识图模型（毫秒级），**禁止扩散模型**（会生成假证据） |
| 工具只查询 | 抗幻觉——工具不凭空生成内容，只规约已有证据 |
| 数字锚定 | LLM 分不清左右但分得清数字，用格子编号替代方向描述 |
| 裁剪必杀 | 缩放时裁剪子图再识别，而非整图缩放 |
| 强制 JSON | 每次查询强迫 LLM 输出结构化 JSON，自动注入四叉树网格 |
| AOT 兼容 | 纯计算 + 图像裁剪，无反射 emit，符合 NativeAOT 约束 |

## 2. 核心能力分解

### 2.1 视觉标注工具（画笔）— M1

**核心机制：四叉树 + 画格子 + 格子左下角编号 + 递归编码 + 染色**

#### 2.1.1 四叉树网格编码
- 将图片递归四等分，每个格子用**左下角为原点**的编号体系
- 编码格式：`L0` 根 → `L0.0 / L0.1 / L0.2 / L0.3`（四象限，左下起算）→ `L0.0.0 ...` 逐层细分
- 层数即缩放精度，编码即定位路径，可任意深度递归
- 编码是**稳定锚点**：LLM 引用 `L0.2.1` 比引用"右上方那个东西"可靠

#### 2.1.2 染色与虚线标记
- 每个格子可染色（颜色深度 = 标注强度）
- 结构化 JSON 自动注入网格，利用**颜色深度**绘制虚线标记
- 线性参数 `alpha`：`-1` = 隐藏（无色），`0..1` = 标记强度
- 缩放时虚线按线性比例重绘

#### 2.1.3 指示屏幕功能
- 在屏幕上叠加显示标注网格（指示当前观察区域）

#### 2.1.4 工具契约（草案）
```
annotate_image:
  输入: image_path | base64, depth(四叉树层数), focus_cell(聚焦格编码, 可空)
  输出: { grid: [{code, x, y, w, h, alpha}], image_width, image_height, focused_region_base64? }

zoom_cell:
  输入: image_path, cell_code, target_depth
  输出: 裁剪该格子子图 → 重新四叉编码 → 返回新网格 + 子图 base64
```

### 2.2 隐喻拓扑（分层展开）— M2

**核心机制：图片细节 → 超图/知识图谱 → 按标签触发分层下钻**

- 顶层识别返回粗粒度标签（如"火影忍者十二小强集合照"）
- 触发"人物"标签 → 自动要求下钻：表情、发型、衣着、站姿、情绪
- **膨胀控制**：每层下钻有预算上限，超限停止（待定策略）

> 复用现有 `ToolHypergraph` / `ToolHyperedge` 模型，但语义从"工具链关联"扩展为"图片细节关联"。

### 2.3 时序隐喻（多帧纠偏）— M3

**核心机制：拒绝单帧误导，多帧时序聚合提取稳定结构**

- 单帧可嵌入文字误导，多帧交叉验证可滤除杂音
- 提取多帧间**稳定中间轮廓**（杂音中不变的结构 = 真实信号）
- 输出结构化时序表达

### 2.4 测量 — M4

| 维度 | 方法 | 备注 |
|------|------|------|
| 长度 | 不等比缩放预处理 → 1:1 识别；参考物（标准人民币/身份证尺寸）标定 | 可能引入幻觉，可接受 |
| 进深（颜色） | 颜色梯度推断深度 | 对齐谢赛宁香蕉模型思路 |
| 非等比 | 长宽比、透视、高维变换 | **高维/保角变换无现成工具，可能超出工具层能力** |

## 3. 技术约束

| 约束 | 说明 |
|------|------|
| 目标框架 | net10.0 + NativeAOT |
| 禁止 | 扩散模型、反射 emit、dynamic |
| 图像处理 | 复用现有 `ImageResizer` / `ImageMediaTypeHelper`（Hands 已有） |
| 多模态调用 | 复用 `IQueryService` + `ModelModalityKind.ReadImage` |
| 工具注册 | `[McpTool]` 特性 + 源码生成器，禁止手写 IToolHandler |
| 容器 | 查找集用 `FrozenSet`/`FrozenDictionary`，禁止 `List.Contains` |

## 4. 架构落点（独立工程/插件 — 用户指定）

> 用户决策：独立工程实现，作为插件，隔离渲染依赖（SkiaSharp/ImageSharp.Drawing 只影响该工程，不污染 Hands）。

| 层 | 位置 | 内容 |
|----|------|------|
| Abstractions | `foundation/Abstractions/06-perception/Vision/` | `IQuadtreeAnnotator`、`IImageMeasurer`、`ITemporalFrameAggregator` 接口 + DTO |
| **独立工程** | `core/execution/Vision/src/Vision.csproj`（待确认位置） | 四叉树编码器、图像裁剪器、标注渲染器、`QuadtreeAnnotationToolHandlers`（McpTool） |
| 渲染依赖 | 独立工程 csproj 内 | SkiaSharp 或 ImageSharp.Drawing（隔离在此工程） |
| 枚举 | `ToolCategory` 新增 `Vision` | 需全量重建生成器 |
| 超图预设 | `ToolHypergraphPresets` | 若 M2 落地，新增图片细节超边预设 |

**加载方式（待确认）**：
- 方案A：内置静态引用 — 主工程引用 Vision.csproj，`[Register]`+DI 自动注册，`[McpTool]` 源码生成器扫描。最简单、AOT 友好
- 方案B：外部进程插件 — 通过 `IPluginManager` 加载独立进程，IPC 通信。隔离更强但通信开销大、需处理图片传输

> 现有 `MultimodalUiElementDetector` 保留，新工具与之**互补**：现有做整图 UI 元素识别，新工具做四叉树递归深挖。
> 项目插件系统：`IPluginManager`(infrastructure/Infrastructure/Plugins/) 支持内置工作流插件 + 外部进程插件(PID)、热重载、Hook 注入、命令注册。

## 5. 思维缺陷与待确认问题

> 以下是需要用户决策的关键缺陷，阻断直接进入实现。

### D1. 四叉树编码细节未定
- "格子左下角编号"的具体编码格式？是 `L0.2.1` 这种点分路径，还是 Morton/Z-order 码？
- 象限排序：左下=0、右下=1、左上=2、右上=3？还是其他序？LLM 对序号语义需明确约定

### D2. "染色"语义不明
- 染色是**给 LLM 返回的 JSON 里标注 alpha 值**，还是**真的在图片像素上画色块/虚线再返回 base64**？
- 若是后者，渲染管线用什么？SkiaSharp（AOT 兼容性需验证）还是 GDI？

### D3. "指示屏幕"边界
- 是在桌面屏幕上叠加透明窗口显示网格（侵入式 GUI），还是只返回标注后的图片让调用方自行显示？

### D4. 隐喻拓扑膨胀控制
- 每层下钻的停止条件？token 预算？层数上限？还是用户手动叫停？
- "自动要求获取表情/发型/衣着"——这个"自动"是工具层硬编码触发，还是 LLM 自主决定下钻？

### D5. 时序隐喻"杂音中间轮廓"定义模糊
- "多帧之间稳定的中间轮廓"具体提取算法？光流？帧差？还是再次依赖识图模型对比？
- 帧来源：视频抽帧？连续截图？用户提供图片序列？

### D6. 测量参考物合规性
- "标准人民币长度、身份证长度"作为标定参考——硬编码真实尺寸是否合规？是否改为用户传入参考物尺寸？

### D7. 高维/保角变换超出工具层
- 用户自述"高维变换、保角变换还没有工具，可能涉及模型本身联想，并非工具层可以做到"
- **建议**：M4 仅落地长度+进深，高维/保角变换标记为"模型层能力，不在本工具范围"

### D8. 与现有 MultimodalUiElementDetector 的关系
- 是互补（保留两者）还是替代？初步判断互补，但需确认是否要统一坐标体系

### D9. 输入输出契约
- 输入统一用 `image_path` 还是 `base64`？还是两者皆可（对齐现有 `ReadImageFileAsync`）？
- 标注结果是否要回写图片文件，还是只返回结构化 JSON + 可选 base64？

### D10. 范围与优先级
- M1~M4 是否全部一期落地？还是 M1（画笔）先行，M2~M4 后续？
- 本 PRD 是否只到设计阶段，还是要直接进入 TDD 实现？

## 7. MCP 工具清单（细粒度，单一职责）

共 12 个工具，统一归入新分组 `Vision`（`ToolCategory` 新增枚举值，需全量重建生成器）。

| # | 工具名 | 模块 | 职责 |
|---|--------|------|------|
| 1 | `quadtree_build` | M1 | 对图片构建指定层数四叉树网格，返回所有格子编码+坐标 |
| 2 | `quadtree_zoom` | M1 | 聚焦指定格子，裁剪子图，返回子图 base64+新网格 |
| 3 | `quadtree_paint` | M1 | 给指定格子染色（设 alpha），可批量 |
| 4 | `quadtree_render` | M1 | 把染色/虚线标注渲染到图片，返回 base64 |
| 5 | `screen_indicate` | M1 | 在屏幕上叠加指示当前观察区域 |
| 6 | `image_describe` | M2 | 顶层粗粒度识别，返回标签 |
| 7 | `image_drill_down` | M2 | 按标签下钻获取细粒度属性 |
| 8 | `temporal_aggregate` | M3 | 多帧时序聚合 |
| 9 | `temporal_stable_contour` | M3 | 提取稳定中间轮廓 |
| 10 | `measure_length` | M4 | 长度测量（参考物标定） |
| 11 | `measure_depth` | M4 | 颜色进深测量 |
| 12 | `measure_ratio` | M4 | 长宽比/非等比测量 |

## 8. 四叉树编码与方位设计（已确认）

采用 **D 方案：数字编码 + quadrant 字母语义 + neighbor 工具** 混合。

### 8.1 格子编码
- **编码**：数字点分路径 `L0.2.1`（简洁，LLM 引用方便）
- **象限序**：左下起算（对齐"格子左下角编号"原则）：`SW=0, SE=1, NW=2, NE=3`
- **层数** = 缩放精度，编码深度可任意递归

### 8.2 格子元数据（quadtree_build / quadtree_zoom 返回）
```json
{
  "code": "L0.2.1",
  "quadrant": "NE",          // 相对父格子的象限(4 选 1)：SW/SE/NW/NE
  "region": "右上",          // 相对根的累积方位(中文语义，LLM 直观)
  "x": 100, "y": 50, "w": 25, "h": 25,
  "alpha": -1                // -1=隐藏(无色)，0..1=标注强度
}
```

### 8.3 八方位邻居查询（新增工具，补入工具清单第 13 项）
- `quadtree_neighbor(code, direction)`：direction ∈ {N,S,W,E,NW,NE,SW,SE}，返回同层邻居格子 code（边界外返回 null）
- 用途：LLM 主动导航空间关系，按需查询不膨胀 build 返回体

### 8.4 设计要点
- 数字编码保简洁（引用），quadrant/region 保方位感知（理解），neighbor 工具保探索（导航）
- 三者互补：LLM 分不清左右但分得清数字 → 数字定位 + 字母语义辅助 + 工具查询纠偏

### 8.5 渲染规格（用户指定）
每次图像生成（quadtree_render）必须满足：
1. **虚线**（dashed line）— 标注网格用虚线，非实线
2. **线性比例宽度** — 缩放时线宽 = baseWidth × scale，格子越小线越细
3. **透明度** — alpha: `-1`=隐藏(无色)，`0..1`=标注强度，让 AI 能看透标注后面的原图内容

### 8.6 渲染库现状（待澄清）
- 项目现有 Skia 仅在 `tests\Unit\JoinCodeGui.Tests`（通过 `Avalonia.Skia`，GUI 测试层 Avalonia 渲染后端），**非独立 SkiaSharp 包**
- 主工程 `Hands`(core 层) 用 `SixLabors.ImageSharp 3.1.12`（无 Drawing 包）
- 在 Hands 工具层画虚线网格，需新引入独立 `SkiaSharp` 包 或 `SixLabors.ImageSharp.Drawing` 包

## 9. 决策记录

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: PRD 初稿落盘到项目根目录，对齐已有 RangeDownloader-PRD.md 命名风格 -->
<!-- 原因: 规划阶段未改动源码，无需编译；思维缺陷需用户确认后才进入实现 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 12 个细粒度 MCP 工具，统一归入新建 ToolCategory.Vision 分组 -->
<!-- 原因: 图片标注/测量/时序与 DesktopControl(操作鼠标键盘) 语义不同，与 Analytics 部分重叠但不够内聚；单分组注册简单、语义清晰 -->
<!-- 替代方案: 按子域拆 4 个分组(粒度最细但 4 个新枚举)、融入 DesktopControl(语义污染)、Vision+测量入 Analytics(折中) -->
<!-- 影响: ToolCategory 新增 Vision 枚举值 → 需全量重建 Generators.slnx → Foundation 起全部重编 -->
<!-- 待确认: D1~D10 中 D10(实现范围) 最阻断，其次 D2(染色语义)、D1(编码格式)、D7(高维变换) -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 所有 LLM 返回的 JSON 一律复用 LlmJsonHelper(foundation/Abstractions/00-core/Core/Utils/LlmJsonHelper.cs) -->
<!-- 原因: LlmJsonHelper 是全局统一门控(ExtractJsonBlock→ExtractInlineJson→RepairJson 三层宽容)，注释强制要求"所有 LLM 结构化 JSON 必须通过此入口" -->
<!-- 反例: 现有 MultimodalUiElementDetector.ExtractJson / ObservationLearner.ExtractJson 各自重复实现，新工具不复刻此反模式 -->
<!-- 同类复用: JsonLenientCoercer(字段级宽容) 同目录，按需引用 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 染色 = 始终渲染像素 + 返回 base64（用户选定） -->
<!-- 待定: 渲染库选择 — ImageSharp.Drawing(复用现有 ImageSharp 3.1.12 生态) vs SkiaSharp(新引入) -->
<!-- 约束: 项目已用 SixLabors.ImageSharp 3.1.12，根目录 IsAotCompatible=true/PublishAot=true，Hands 在 core 层(子层覆盖 false 但最终被 app 层 AOT exe 引用，故依赖库必须 AOT 兼容) -->
<!-- 风险: ImageSharp 核心包无绘图能力，画虚线网格需 ImageSharp.Drawing 包(尚未引用) -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 独立工程 + 内置静态引用加载（用户选定） -->
<!-- 原因: 主工程引用 Vision.csproj，[Register]+DI 自动注册 IQueryService/IFileSystem，进程内调用 LLM/读图无跨进程开销，AOT 友好；依赖隔离在 csproj 内已达成 -->
<!-- 替代方案: 外部进程插件(隔离更强但图片/LLM 跨进程传输开销大、需设计协议) -->
<!-- 待确认: 工程位置(core/execution/Vision vs services/Vision) + 渲染库(SkiaSharp vs ImageSharp.Drawing) -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 渲染库 = SkiaSharp（用户选定，性能高/硬件加速，与现有 Avalonia.Skia 同生态） -->
<!-- 风险: SkiaSharp 需 libskia 原生库 + SkiaSharp.NativeAssets，NativeAOT 兼容性需实现前单独验证(项目硬约束 PublishAot=true) -->
<!-- 验证计划: 按 AGENTS.md 复杂任务规范，新建卫星测试项目验证 SkiaSharp 3.x NativeAOT + TrimMode=full，通过后再引入主工程 -->
<!-- 隔离: 独立工程已隔离原生依赖，SkiaSharp 仅影响 Vision.csproj，不污染 Hands/其他 core 工程 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 工程位置 = services/Vision（用户选定，与 Eyes/Mcp 平级，services 层） -->
<!-- 依赖链: services/Vision → core → infrastructure → foundation；编译顺序在 Services.slnx -->
<!-- 注册: [Register]+DI 自动注册 + [McpTool] 源码生成器扫描，主工程静态引用 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: 输入输出契约 = path+base64 双输入，输出 JSON+渲染 base64（用户选定，对齐现有 ReadImageFileAsync） -->
<!-- M1 设计完整性: 编码(数字路径+quadrant+neighbor) + 染色(始终渲染base64) + 渲染(SkiaSharp虚线+线性比例宽度+透明度) + 工程(services/Vision独立) + 契约(path+base64) + JSON(LlmJsonHelper) → M1 可进入 TDD -->
<!-- M2~M4 待定细节: D4(膨胀控制) D5(时序算法) D6(参考物合规) D7(高维变换) D3(screen_indicate语义) D1(象限序确认) -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M2 下钻触发 = 工具建议 + LLM 决定（用户选定） -->
<!-- 机制: image_drill_down 工具返回 suggested_attributes(常见标签→推荐属性映射)，LLM 采纳或自定义实际下钻属性 -->
<!-- 原因: 硬编码不灵活(不同图片下钻不同属性)，纯 LLM 自主无引导；工具建议+LLM 决定兼顾灵活与引导 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M2 膨胀停止 = token 预算硬上限 + 层数上限双保险（用户选定） -->
<!-- 机制: 单次下钻输出≤token 上限(默认 2000)，下钻深度≤层数上限(默认 3)；超限返回截断+提示 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M3 时序隐喻 = 纵深防御链（用户指定） -->
<!-- L1: 多模态模型原生 ReadGif/ReadVideo → 提示词约束，直接喂原文件，零抽帧 -->
<!-- L2: 模型不支持 → 复用现有 ModalityMismatchMessageBuilder 提示切换 → 请求用户确认是否下推(防烧钱) -->
<!-- L3: 下推抽帧 → 新建 GIF/视频抽帧/连续截屏 → 图片序列 → 逐帧识别 -->
<!-- 现状差距: 项目有 ReadGif/ReadVideo 模态枚举 + MediaIntentDetector 意图检测 + 不匹配提示，但无实际 GIF/视频抽帧实现、无连续截屏(仅单次 GdiScreenCaptureService) → L3 抽帧能力需新建 -->
<!-- 待确认: D5b(稳定轮廓算法) -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M3 稳定轮廓 = 帧差粗筛 + 识图确认（用户选定） -->
<!-- 机制: L1 帧差法(ImageSharp 可做,无 LLM,毫秒)粗筛稳定区域(差值小=真实轮廓) → L2 识图模型(复用 MultimodalUiElementDetector)精确认证(多帧交叉验证) -->
<!-- 原因: 帧差快速定位稳定区，识图赋予语义并抗幻觉；两层结合最准 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M4 参考物标定 = 不内置，LLM 联网检索（用户选定，国际化考虑） -->
<!-- 机制: measure_length 接受参考物描述(如"一张美元纸币") + 图中位置(格子编码) → LLM 联网检索该参考物标准尺寸 → 标定比例尺 → 测量目标 -->
<!-- 原因: 硬编码人民币/身份证约束了中国，项目国际化；尺寸均为公开信息，LLM 可联网查任意国家参考物 -->
<!-- 依赖: 项目已有 WebSearch/WebFetch 能力 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: M4 高维/保角变换 = 排除，标注为"模型层能力，不在工具范围"（用户选定） -->
<!-- 范围: M4 仅 measure_length(长度) + measure_depth(颜色进深) + measure_ratio(长宽比/非等比) -->
<!-- 原因: 用户自述"高维/保角变换可能涉及模型本身联想，并非工具层可以做到" -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: D3 screen_indicate = 返回标注图片 base64（用户选定），不侵入桌面 -->
<!-- 与 quadtree_render 区分: screen_indicate 特化为只高亮当前观察区域(zoom 格子)，quadtree_render 渲染所有染色 -->

<!-- 🤖 Auto Decision: 2026-08-28 -->
<!-- 决策: D1 象限序 = SW=0, SE=1, NW=2, NE=3（左下起算，符合"格子左下角编号"原则，已假定） -->
<!-- 若有异议可在实现阶段调整，不影响整体架构 -->

<!-- 🤖 设计定稿总结: M1(画笔5+neighbor) + M2(拓扑2) + M3(时序2) + M4(测量3) = 13 工具，services/Vision 独立工程，SkiaSharp 渲染，LlmJsonHelper 复用，path+base64 双输入 -->
