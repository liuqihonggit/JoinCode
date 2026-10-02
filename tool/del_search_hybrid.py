"""删除 SearchHybridAsync 方法（保留 SearchDocumentAsync）。"""
import pathlib, sys

f = pathlib.Path(r"D:\project\w2\kit\mcp_tool_dispatch\code_tools\CodeIndexToolHandlers.cs")
content = f.read_text(encoding='utf-8')

start_marker = "    /// <summary>\n    /// 混合检索"
end_marker = "    /// <summary>\n    /// 文档检索"

try:
    start = content.index(start_marker)
    end = content.index(end_marker)
except ValueError as e:
    print(f"ERROR: {e}", file=sys.stderr)
    sys.exit(1)

new_content = content[:start] + content[end:]
f.write_text(new_content, encoding='utf-8')
print(f"Deleted {end - start} chars (SearchHybridAsync method)")
