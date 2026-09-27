"""
批量替换 HandleAsync → Handle
- 同步: ValueTask HandleAsync( → void Handle(, 删除 return ValueTask.CompletedTask
- 异步: async ValueTask HandleAsync( → void Handle( + _ = HandleAsyncImpl(...), 原方法体改为 private async ValueTask HandleAsyncImpl

用法:
  python tool/migrate_handle_async.py --dry-run   # 预览
  python tool/migrate_handle_async.py              # 执行
"""
import re, os, sys

DRY_RUN = '--dry-run' in sys.argv or '--preview' in sys.argv

SKIP_DIRS = {'.xxx', 'artifacts', 'bin', 'obj', '.git', 'node_modules'}

def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith('.cs'):
                result.append(os.path.join(dirpath, f))
    return result

def find_method_end(content, brace_start):
    """从 { 开始，找到匹配的 } 位置"""
    depth = 1
    pos = brace_start + 1
    while depth > 0 and pos < len(content):
        c = content[pos]
        if c == '{': depth += 1
        elif c == '}': depth -= 1
        pos += 1
    return pos  # } 的下一个位置

def extract_param_names(params_str):
    """从参数列表提取参数名: 'MyCmd cmd, CancellationToken ct' → 'cmd, ct'"""
    names = []
    for param in params_str.split(','):
        param = param.strip()
        if param:
            parts = param.split()
            names.append(parts[-1].rstrip(')'))
    return ', '.join(names)

def extract_params(signature):
    """从方法签名提取参数列表"""
    start = signature.index('(') + 1
    depth = 1
    pos = start
    while depth > 0 and pos < len(signature):
        c = signature[pos]
        if c == '(': depth += 1
        elif c == ')': depth -= 1
        pos += 1
    return signature[start:pos-1]

def process_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original = content
    changes = []
    
    # 1. 处理同步: protected override ValueTask HandleAsync(
    sync_pattern = r'protected override ValueTask HandleAsync\('
    for match in re.finditer(sync_pattern, content):
        changes.append(f"  SYNC: {filepath}:{content[:match.start()].count(chr(10))+1}")
    
    content = re.sub(
        r'protected override ValueTask HandleAsync\(',
        'protected override void Handle(',
        content
    )
    # 删除 return ValueTask.CompletedTask; 和 return default;
    content = re.sub(r'\n\s*return ValueTask\.CompletedTask;\s*\n', '\n', content)
    
    # 2. 处理异步: protected override async ValueTask HandleAsync(
    async_pattern = r'protected override async ValueTask HandleAsync\('
    matches = list(re.finditer(async_pattern, content))
    
    for match in reversed(matches):
        line_no = content[:match.start()].count('\n') + 1
        changes.append(f"  ASYNC: {filepath}:{line_no}")
        
        # 找到 { 
        brace_pos = content.index('{', match.start())
        # 找到方法结束 }
        end_pos = find_method_end(content, brace_pos)
        
        # 提取签名（从 match.start 到 brace_pos）
        signature = content[match.start():brace_pos]
        params = extract_params(signature)
        param_names = extract_param_names(params)
        
        # 提取方法体（不含外层 {}）
        body = content[brace_pos+1:end_pos-1]
        
        # 构建新代码
        indent = '    '  # 假设 4 空格缩进
        new_handle = f"protected override void Handle({params}) {{\n{indent}_ = HandleAsyncImpl({param_names});\n    }}"
        new_impl = f"private async ValueTask HandleAsyncImpl({params}) {{{body}\n    }}"
        
        replacement = new_handle + '\n\n    ' + new_impl
        content = content[:match.start()] + replacement + content[end_pos:]
    
    if content != original:
        if DRY_RUN:
            print(f"[DRY-RUN] {filepath}")
            for c in changes:
                print(c)
        else:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            print(f"[DONE] {filepath}")
            for c in changes:
                print(c)
    return content != original

def main():
    files = find_cs_files('.')
    total = 0
    for f in files:
        if process_file(f):
            total += 1
    mode = "DRY-RUN" if DRY_RUN else "DONE"
    print(f"\n{mode}: {total} files changed")

if __name__ == '__main__':
    main()
