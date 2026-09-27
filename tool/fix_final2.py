"""
最终修复:
1. xxx.Tell(...).ConfigureAwait(false) → xxx.Tell(...)  (去掉 void 后的 ConfigureAwait)
2. HttpClient/httpClient/_httpClient/client.Tell( → .SendAsync(  (恢复 HttpClient)
3. Tell(cmd, ct) → Tell(cmd)  (去掉 Tell 的 ct 参数)
"""
import re, os, sys

def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in {'.xxx', 'artifacts', 'bin', 'obj', '.git'}]
        for f in files:
            if f.endswith('.cs'):
                result.append(os.path.join(dirpath, f).replace('\\', '/'))
    return result

def remove_tell_configure_await(content):
    """去掉 Tell(...).ConfigureAwait(false) 的 .ConfigureAwait(false)"""
    result = []
    i = 0
    while i < len(content):
        idx = content.find('Tell(', i)
        if idx == -1:
            result.append(content[i:])
            break
        # 检查前面是否是 . （方法调用）
        is_method_call = idx > 0 and content[idx-1] == '.'
        # 找匹配的 )
        depth = 1
        j = idx + 4
        while depth > 0 and j < len(content):
            if content[j] == '(': depth += 1
            elif content[j] == ')': depth -= 1
            j += 1
        # j 是 ) 的下一个位置
        # 检查后面是否是 .ConfigureAwait(false)
        if content[j:j+25].startswith('.ConfigureAwait(false)'):
            result.append(content[i:j])
            i = j + 25  # 跳过 .ConfigureAwait(false)
        else:
            result.append(content[i:j])
            i = j
    return ''.join(result)

def restore_httpclient_sendasync(content):
    """恢复 HttpClient.SendAsync（不是 ActorBase.Tell）"""
    # 各种 HttpClient 变量名
    for var in ['httpClient', '_httpClient', 'client', '_client', 'HttpClient']:
        content = content.replace(f'{var}.Tell(', f'{var}.SendAsync(')
    return content

def fix_tell_two_params(content):
    """Tell(xxx, ct) → Tell(xxx) / Tell(xxx, cancellationToken) → Tell(xxx)"""
    # 用括号匹配
    result = []
    i = 0
    while i < len(content):
        idx = content.find('Tell(', i)
        if idx == -1:
            result.append(content[i:])
            break
        # 找匹配的 )
        depth = 1
        j = idx + 4
        while depth > 0 and j < len(content):
            if content[j] == '(': depth += 1
            elif content[j] == ')': depth -= 1
            j += 1
        inner = content[idx+4:j-1]
        # 如果最后是 , ct 或 , cancellationToken，去掉
        if re.search(r',\s*(?:ct|cancellationToken)\s*$', inner):
            inner = re.sub(r',\s*(?:ct|cancellationToken)\s*$', '', inner)
        result.append(content[i:idx+4])
        result.append(inner)
        result.append(')')
        i = j
    return ''.join(result)

total = 0
for f in find_cs_files():
    with open(f, 'r', encoding='utf-8') as fh:
        content = fh.read()
    original = content
    
    content = remove_tell_configure_await(content)
    content = restore_httpclient_sendasync(content)
    content = fix_tell_two_params(content)
    
    if content != original:
        with open(f, 'w', encoding='utf-8') as fh:
            fh.write(content)
        total += 1

print(f'DONE: {total} files changed')
