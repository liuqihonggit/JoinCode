#!/usr/bin/env python3
"""推广 GitHubCommonOptions — 将 repo+working_dir+verbosity+json_fields 四参数替换为 [McpToolOptions] GitHubCommonOptions? common = null"""
import re, sys, pathlib

# 匹配方法签名中连续的四参数块（可能有不同顺序）
# 常见模式: json_fields, verbosity, repo, working_dir 或 repo, working_dir, verbosity, json_fields
PARAM_PATTERN = re.compile(
    r'(?P<indent>[ \t]*)\[McpToolParameter\(WellKnownParam\.(?P<p1>JsonFields|Verbosity|Repo|WorkingDir)\)\]\s+(?P<t1>\S+)\s+(?P<n1>\w+)\s*=\s*null,\s*\n'
    r'[ \t]*\[McpToolParameter\(WellKnownParam\.(?P<p2>JsonFields|Verbosity|Repo|WorkingDir)\)\]\s+(?P<t2>\S+)\s+(?P<n2>\w+)\s*=\s*null,\s*\n'
    r'[ \t]*\[McpToolParameter\(WellKnownParam\.(?P<p3>JsonFields|Verbosity|Repo|WorkingDir)\)\]\s+(?P<t3>\S+)\s+(?P<n3>\w+)\s*=\s*null,\s*\n'
    r'[ \t]*\[McpToolParameter\(WellKnownParam\.(?P<p4>JsonFields|Verbosity|Repo|WorkingDir)\)\]\s+(?P<t4>\S+)\s+(?P<n4>\w+)\s*=\s*null,',
    re.MULTILINE
)

REQUIRED_SET = {'JsonFields', 'Verbosity', 'Repo', 'WorkingDir'}

def process_file(filepath):
    content = filepath.read_text(encoding='utf-8')
    if 'GitHubCommonOptions' in content and 'McpToolOptions' in content:
        # 已有部分推广，跳过避免重复
        pass
    
    matches = list(PARAM_PATTERN.finditer(content))
    if not matches:
        return 0
    
    replacements = []
    for m in matches:
        params = {m.group('p1'), m.group('p2'), m.group('p3'), m.group('p4')}
        if params != REQUIRED_SET:
            continue
        
        names = {
            m.group('p1'): m.group('n1'),
            m.group('p2'): m.group('n2'),
            m.group('p3'): m.group('n3'),
            m.group('p4'): m.group('n4'),
        }
        indent = m.group('indent')
        replacement = f'{indent}[McpToolOptions] GitHubCommonOptions? common = null,'
        replacements.append((m.start(), m.end(), m.group(0), replacement, names))
    
    if not replacements:
        return 0
    
    # 从后往前替换，避免偏移变化
    for start, end, old_text, new_text, names in reversed(replacements):
        content = content[:start] + new_text + content[end:]
    
    # 替换方法体中的变量引用
    for _, _, _, _, names in replacements:
        for param_name, var_name in names.items():
            if param_name == 'Repo':
                content = content.replace(f' {var_name},', f' common?.Repo,', 1) if var_name == 'repo' else content
                content = content.replace(f'({var_name},', f'(common?.Repo,', 1) if var_name == 'repo' else content
            elif param_name == 'WorkingDir':
                content = content.replace(f' {var_name},', f' common?.WorkingDir,', 1) if var_name == 'working_dir' else content
            elif param_name == 'Verbosity':
                content = content.replace(f', {var_name},', f', common?.Verbosity,', 1) if var_name == 'verbosity' else content
            elif param_name == 'JsonFields':
                content = content.replace(f', {var_name},', f', common?.JsonFields,', 1) if var_name == 'json_fields' else content
    
    filepath.write_text(content, encoding='utf-8')
    return len(replacements)

if __name__ == '__main__':
    files = sys.argv[1:]
    total = 0
    for f in files:
        p = pathlib.Path(f)
        count = process_file(p)
        if count:
            print(f"{f}: {count} 处推广")
            total += count
    print(f"总计: {total} 处推广")
