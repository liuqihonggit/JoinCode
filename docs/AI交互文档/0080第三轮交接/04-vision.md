# 交接文档 04: Vision 图片工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（13个）

1. analyze_image - 分析图片提取信息
2. compare_images - 对比两张图片
3. detect_objects - 检测图片中物体
4. extract_text - OCR提取文字
5. find_text_in_image - 在图片中查找文字
6. get_image_info - 获取图片元信息
7. image_to_text - 图片转文字描述
8. measure_similarity - 测量图片相似度
9. resize_image - 调整图片大小
10. crop_image - 裁剪图片
11. convert_image - 转换图片格式
12. detect_anomaly - 检测图片异常
13. generate_image_caption - 生成图片描述

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
