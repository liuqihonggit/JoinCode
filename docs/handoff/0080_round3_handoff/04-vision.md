# 交接文档 04: Vision 图片工具

> ✅ 全部 13 个工具已验证通过（2026-09-09）。无修复需要。

## 工具列表（13个）— 全部已验证 ✅

1. image_describe ✅ — 返回图片描述+10个标签
2. image_drill_down ✅ — 按标签下钻返回10个属性+置信度
3. measure_depth ✅ — 返回颜色统计+深度估计
4. measure_length ✅ — 返回像素距离+角度
5. measure_ratio ✅ — 返回长宽比+简化比+常见比例匹配
6. quadtree_build ✅ — 返回16格子编码+坐标+方位
7. quadtree_neighbor ✅ — 返回邻居格子编码
8. quadtree_paint ✅ — 批量染色返回更新网格
9. quadtree_render ✅ — 渲染网格叠加返回标注图片base64
10. quadtree_zoom ✅ — 聚焦格子裁剪子图重建网格
11. screen_indicate ✅ — 高亮格子返回标注图片base64
12. temporal_aggregate ✅ — 多帧时序分析返回运动轨迹报告
13. temporal_stable_contour ✅ — 提取稳定区域轮廓返回掩码图base64

## 测试方法

使用 `libs/Termal.Gui/docfx/images/logo48.png`（48x48, 2143字节）作为测试图片，base64 编码后通过 `--args-file` 传递参数（避免命令行长度限制）。

## 测试命令

### 准备测试图片
```powershell
# 先用 screenshot 工具生成一张测试图片
jcc mcp_call screenshot --% "{\"target\":\"screen\",\"output_file\":\"test-image.png\"}"
```

### 1. analyze_image
```powershell
jcc mcp_schema analyze_image
jcc mcp_call analyze_image --% "{\"image_path\":\"test-image.png\",\"task\":\"描述图片内容\"}"
```
预期：返回图片内容描述

### 2. compare_images
```powershell
jcc mcp_schema compare_images
jcc mcp_call compare_images --% "{\"image1\":\"test-image.png\",\"image2\":\"test-image.png\"}"
```
预期：返回相似度100%

### 3. extract_text (OCR)
```powershell
jcc mcp_schema extract_text
jcc mcp_call extract_text --% "{\"image_path\":\"test-image.png\"}"
```
预期：返回图片中的文字

### 4. get_image_info
```powershell
jcc mcp_schema get_image_info
jcc mcp_call get_image_info --% "{\"image_path\":\"test-image.png\"}"
```
预期：返回宽度/高度/格式等元信息

### 其余工具依此类推，先 jcc mcp_schema <tool> 查看参数，再调用

## 验收标准

- 图片不存在时友好报错
- OCR 结果准确
- 对比结果有数值相似度
- 超过30s的工具必须备注
