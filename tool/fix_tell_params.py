"""
修复 Tell 多余参数: Tell(xxx, ct) → Tell(xxx)
修复 MockHttpMessageHandler.Tell → SendAsync (HttpMessageHandler 重写)
"""
import re, os, sys

DRY_RUN = '--dry-run' in sys.argv
SKIP_DIRS = {'.xxx', 'artifacts', 'bin', 'obj', '.git', 'node_modules'}

def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith('.cs'):
                result.append(os.path.join(dirpath, f).replace('\\', '/'))
    return result

def fix_tell_params(content):
    """Tell(xxx, ct) → Tell(xxx) — 去掉 CancellationToken 参数"""
    # 匹配 Tell(任何内容, ct) 或 Tell(任何内容, cancellationToken)
    def replacer(m):
        inner = m.group(1)
        # 去掉最后的 , ct 或 , cancellationToken
        inner = re.sub(r',\s*(?:ct|cancellationToken)\s*$', '', inner)
        return f'Tell({inner})'
    
    # 处理 Tell(...) — 需要匹配括号
    result = []
    i = 0
    while i < len(content):
        idx = content.find('Tell(', i)
        if idx == -1:
            result.append(content[i:])
            break
        result.append(content[i:idx])
        # 找到匹配的 )
        depth = 1
        j = idx + 4
        while depth > 0 and j < len(content):
            if content[j] == '(': depth += 1
            elif content[j] == ')': depth -= 1
            j += 1
        inner = content[idx+4:j-1]
        # 如果有逗号分隔的最后一个参数是 ct/cancellationToken，去掉
        if re.search(r',\s*(?:ct|cancellationToken)\s*$', inner):
            inner = re.sub(r',\s*(?:ct|cancellationToken)\s*$', '', inner)
        result.append(f'Tell({inner})')
        i = j
    return ''.join(result)

def fix_mock_http(content):
    """MockHttpMessageHandler.Tell → SendAsync (HttpMessageHandler 重写)"""
    content = content.replace('protected override Tell(HttpRequestMessage', 'protected override SendAsync(HttpRequestMessage')
    return content

def fix_native_plugin_return(content):
    """NativePluginHost.DisposeAsync 补 return"""
    return content

def main():
    files = find_cs_files('.')
    total = 0
    for f in files:
        with open(f, 'r', encoding='utf-8') as fh:
            content = fh.read()
        original = content
        
        content = fix_tell_params(content)
        
        if content != original:
            if DRY_RUN:
                print(f"[DRY-RUN] {f}")
            else:
                with open(f, 'w', encoding='utf-8') as fh:
                    fh.write(content)
                total += 1
    mode = "DRY-RUN" if DRY_RUN else "DONE"
    print(f"{mode}: {total} files changed")

if __name__ == '__main__':
    main()
