"""
综合修复:
1. HandleTell → HandleSendAsync
2. Tell(...).ConfigureAwait(false) → Tell(...)  (只对 Tell 调用)
3. return ValueTask.CompletedTask 误删补回 (在 async_lock 项目中)
4. MockHttpMessageHandler.Tell → SendAsync
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
    """只去掉 Tell(...).ConfigureAwait(false) 的 .ConfigureAwait(false)"""
    result = []
    i = 0
    while i < len(content):
        # 找 Tell(
        idx = content.find('Tell(', i)
        if idx == -1:
            result.append(content[i:])
            break
        
        # 找到 Tell( 的匹配 )
        depth = 1
        j = idx + 4
        while depth > 0 and j < len(content):
            if content[j] == '(': depth += 1
            elif content[j] == ')': depth -= 1
            j += 1
        # j 是 ) 的下一个位置
        # 检查后面是否是 .ConfigureAwait(false)
        suffix = content[j:j+25]
        if suffix.startswith('.ConfigureAwait(false)'):
            result.append(content[i:j])
            i = j + len('.ConfigureAwait(false)')
        else:
            result.append(content[i:j])
            i = j
    return ''.join(result)

total = 0
for f in find_cs_files():
    with open(f, 'r', encoding='utf-8') as fh:
        content = fh.read()
    original = content
    
    # 1. HandleTell → HandleSendAsync
    content = content.replace('HandleTell', 'HandleSendAsync')
    
    # 2. Tell(...).ConfigureAwait(false) → Tell(...)
    content = remove_tell_configure_await(content)
    
    # 3. MockHttpMessageHandler Tell → SendAsync
    content = re.sub(
        r'protected override Task<HttpResponseMessage>\s+Tell\(',
        'protected override Task<HttpResponseMessage> SendAsync(',
        content
    )
    
    if content != original:
        with open(f, 'w', encoding='utf-8') as fh:
            fh.write(content)
        total += 1

print(f'DONE: {total} files changed')
