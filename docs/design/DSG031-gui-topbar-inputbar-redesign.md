# DSG031 — GUI 顶部菜单 + 发送区域模型/推理档位选择器设计

## 背景

用户反馈 deepseek 模型列表展开不了(根因:API Key 未配置,后台拉取跳过)。用户要求重新设计 GUI:
1. 顶部菜单改为类 VSCode 菜单栏
2. 学习 Codex 在发送区域集成模型选择和推理档位调整,带颜色动画

## 设计决策

### 1. 顶部菜单 — 类 VSCode 菜单栏

**现状**: TopBarView 一行挤 13 个控件(文件/模型管理/主题/重新生成/清空/重置/供应商下拉/模型下拉/Mock/无人值守/goal/拦截器/设置)

**目标**: 顶部只留 Logo + 全局菜单(文件/编辑/视图/帮助),操作按钮移到侧边栏或发送区域

**菜单结构**:
- **文件**: 新建会话 / 打开会话 / 保存 Markdown / 导出 / 退出
- **编辑**: 重新生成 / 清空当前会话 / 全部重置 / 撤回
- **视图**: 主题切换 / 侧边栏显隐 / 设置面板 / 字号
- **帮助**: 关于 / 文档 / 快捷键

**移出的控件去向**:
- 供应商下拉 + 模型下拉 → 发送区域(模型选择器 Popup)
- 推理档位 → 发送区域(五色按钮)
- Mock 切换 / 无人值守 / goal / 拦截器 → 侧边栏或视图菜单

### 2. 发送区域 — 模型选择器 + 推理档位选择器

#### 2.1 模型选择器

**形式**: 输入框下方左侧紧凑按钮,点击弹出 Popup 列表

**Popup 列表项**: `[供应商图标] 模型名 (上下文大小)`
- 供应商图标: 6 个 SVG 图标(openai/deepseek/anthropic/zhipu/sensenova/agnes)
- 按供应商分组显示
- 当前选中项高亮

#### 2.2 推理档位选择器

**形式**: 5 个档位按钮(low/medium/high/max/auto),紧凑排列

**配色(Codex 风格五色)**:
| 档位 | 颜色 | Hex |
|------|------|-----|
| low | 绿色 | #10A37F |
| medium | 蓝色 | #3B82F6 |
| high | 橙色 | #F97316 |
| max | 紫色 | #8B5CF6 |
| auto | 灰色 | #6B7280 |

**动画**:
- 选中档位: 按钮填充对应色 + 轻发光(发光边框)
- 未选中: 透明背景 + 灰色文字
- 切换: BrushTransition 渐变动画(Duration=200ms)

### 3. 供应商图标

**来源**:
- openai.svg / deepseek.svg / anthropic.svg — simple-icons(单色矢量)
- zhipu.svg / sensenova.svg / agnes.svg — 首字母+品牌色圆形

**路径**: `app/gui/assets/provider_icons/`

**渲染**: 提取 SVG path data,用 Avalonia PathIcon/Geometry 渲染(无需额外依赖)

## 验收标准

| 需求 | 实现 | 验收 |
|------|------|------|
| 顶部 VSCode 菜单栏 | TopBarView 重构 | 手动 exe 验收 |
| 发送区域模型选择器 | InputBarView Popup | 手动 exe 验收 |
| 推理档位五色按钮 | InputBarView EffortSelector | 手动 exe 验收 |
| 颜色动画 | BrushTransition | 手动 exe 验收 |
| 供应商图标 | 6 个 SVG | 编译+视觉验收 |

<!-- 🤖 Auto Decision: 2026-10-02 -->
<!-- 决策: 用 PathIcon/Geometry 渲染 SVG,不引入 Svg.Skia 依赖 -->
<!-- 原因: 保持 NativeAOT 兼容,减少外部依赖,simple-icons 单色 path 可直接用 Geometry -->
<!-- 替代方案: Svg.Skia 库(增加依赖,可能影响 AOT) -->
