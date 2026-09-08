# 0087. 批量替换 C# 源码禁令与导向

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

PowerShell 文本工具（`Out-File`、`Set-Content`、`[regex]::Replace`、`WriteAllText`）在处理 C# 源码时存在 BOM 写入、文件清空、`$1` 展开为空等问题。本文档定义禁令与正确导向。

## 详细内容

## 批量替换 C# 源码禁令与导向

| ❌ 禁止 | ✅ 导向 |
|---------|---------|
| `Out-File`/`Set-Content` 写 C# 文件 | `ReadAllBytes` → `.Replace()` → `WriteAllBytes` |
| `[regex]::Replace($text, $pat, '$1')` | `.Replace()` 简单替换；必须正则则写 C# 脚本 |
| `[IO.File]::WriteAllText($path, $text)` | `[IO.File]::WriteAllBytes($path, [Encoding]::UTF8.GetBytes($text))` |
| `git show REV:path \| Out-File` | `git show REV:path > local_path`（重定向） |

**原因**: Out-File 写 UTF-8 带 BOM → CS0234；WriteAllText 可能清空文件；`$1` 被 PowerShell 展开为空

## 替代方案

无。字节级读写是避免编码问题的唯一可靠方式。
