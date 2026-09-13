"""
批量修复 AsyncLock 残留的 WaitAsync/Wait/Release 调用。
处理标准模式:
  await _xxx.WaitAsync(...); try { body } finally { _xxx.Release(); }
  → using var guard = await _xxx.LockAsync(...).ConfigureAwait(false); body
  _xxx.Wait(); try { body } finally { _xxx.Release(); }
  → using var guard = _xxx.Lock(); body
"""
import re
import sys
from pathlib import Path

def find_matching_brace(lines, start_idx, open_char='{', close_char='}'):
    """从 start_idx 行的 { 开始，找到匹配的 }"""
    depth = 0
    for i in range(start_idx, len(lines)):
        for ch in lines[i]:
            if ch == open_char:
                depth += 1
            elif ch == close_char:
                depth -= 1
                if depth == 0:
                    return i
    return -1

def extract_brace_content(lines, brace_start_idx):
    """提取从 { 到匹配 } 的内容行（不含外层大括号）"""
    # 找到 { 在 brace_start_idx 行的位置
    line = lines[brace_start_idx]
    brace_pos = line.index('{')
    
    depth = 0
    content_lines = []
    for i in range(brace_start_idx, len(lines)):
        if i == brace_start_idx:
            # 第一行：{ 之后的内容
            after_brace = line[brace_pos+1:]
            if after_brace.strip():
                content_lines.append(after_brace)
            depth = 1
            # 继续计数这一行剩余的括号
            for ch in after_brace:
                if ch == '{':
                    depth += 1
                elif ch == '}':
                    depth -= 1
                    if depth == 0:
                        # 内容在这一行就结束了
                        if content_lines and content_lines[-1] == after_brace:
                            content_lines[-1] = after_brace[:after_brace.rindex('}')]
                        else:
                            content_lines.append(after_brace[:after_brace.rindex('}')])
                        return content_lines, i
        else:
            for ch in lines[i]:
                if ch == '{':
                    depth += 1
                elif ch == '}':
                    depth -= 1
                    if depth == 0:
                        # 这一行的 } 是匹配的
                        before_close = lines[i][:lines[i].rindex('}')]
                        if before_close.strip():
                            content_lines.append(before_close)
                        return content_lines, i
            content_lines.append(lines[i])
    return content_lines, -1

def process_file(filepath):
    """处理单个文件"""
    content = Path(filepath).read_text(encoding='utf-8-sig')
    lines = content.split('\n')
    changes = 0
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        
        # 模式1: await _xxx.WaitAsync(...).ConfigureAwait(false);
        m1 = re.match(r'^(\s*)await\s+(\w+)\.WaitAsync\(([^)]*)\)\.ConfigureAwait\(false\);\s*$', lines[i])
        # 模式1b: await _xxx.WaitAsync(...);
        m1b = re.match(r'^(\s*)await\s+(\w+)\.WaitAsync\(([^)]*)\);\s*$', lines[i])
        # 模式2: _xxx.Wait();
        m2 = re.match(r'^(\s*)(\w+)\.Wait\(\);\s*$', lines[i])
        
        match = m1 or m1b or m2
        if not match:
            i += 1
            continue
        
        indent = match.group(1)
        lock_var = match.group(2)
        
        # 确定锁参数
        if m1:
            lock_args = m1.group(3)
            has_configure = True
        elif m1b:
            lock_args = m1b.group(3)
            has_configure = False
        else:
            lock_args = None  # 同步
        
        # 检查下一行是否是 try
        if i + 1 >= len(lines) or 'try' not in lines[i+1].strip():
            i += 1
            continue
        
        # 找到 try 的 { 行
        try_brace_idx = i + 1
        # try 可能在同一行或下一行有 {
        if '{' not in lines[try_brace_idx]:
            try_brace_idx += 1
        if try_brace_idx >= len(lines) or '{' not in lines[try_brace_idx]:
            i += 1
            continue
        
        # 提取 try body
        try_content, try_end_idx = extract_brace_content(lines, try_brace_idx)
        if try_end_idx == -1:
            i += 1
            continue
        
        # 检查 try_end_idx 之后是否是 finally
        finally_idx = try_end_idx + 1
        if finally_idx >= len(lines) or 'finally' not in lines[finally_idx].strip():
            i += 1
            continue
        
        # 找到 finally 的 { 行
        finally_brace_idx = finally_idx
        if '{' not in lines[finally_brace_idx]:
            finally_brace_idx += 1
        if finally_brace_idx >= len(lines) or '{' not in lines[finally_brace_idx]:
            i += 1
            continue
        
        # 提取 finally body
        finally_content, finally_end_idx = extract_brace_content(lines, finally_brace_idx)
        if finally_end_idx == -1:
            i += 1
            continue
        
        # 检查 finally body 是否只有 _xxx.Release();
        finally_text = '\n'.join(finally_content).strip()
        release_pattern = rf'^{lock_var}\.Release\(\);\s*$'
        if not re.match(release_pattern, finally_text):
            # finally 有其他内容，跳过
            i += 1
            continue
        
        # 构建替换内容
        if lock_args is not None:
            # 异步
            if has_configure:
                new_line = f'{indent}using var guard = await {lock_var}.LockAsync({lock_args}).ConfigureAwait(false);'
            else:
                if lock_args:
                    new_line = f'{indent}using var guard = await {lock_var}.LockAsync({lock_args}).ConfigureAwait(false);'
                else:
                    new_line = f'{indent}using var guard = await {lock_var}.LockAsync().ConfigureAwait(false);'
        else:
            # 同步
            new_line = f'{indent}using var guard = {lock_var}.Lock();'
        
        # 缩进 try body
        body_lines = []
        for bl in try_content:
            body_lines.append(bl)
        
        # 替换从 i 到 finally_end_idx 的所有行
        new_lines = [new_line] + body_lines
        lines[i:finally_end_idx+1] = new_lines
        changes += 1
        # 不增加 i，因为替换后可能还有更多模式
    
    if changes > 0:
        new_content = '\n'.join(lines)
        Path(filepath).write_text(new_content, encoding='utf-8')
        print(f"  {filepath}: {changes} 处替换")
    
    return changes

# 处理命令行参数指定的文件
files = sys.argv[1:]
total = 0
for f in files:
    total += process_file(f)
print(f"总计: {total} 处替换")
