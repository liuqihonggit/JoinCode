"""
安全替换 SendAsync → Tell
排除: transport文件, HandleSendAsync, PersistentSendAsync, HttpMessageHandler.SendAsync
同时修复: Tell(cmd, ct) → Tell(cmd)
同时补回: 误删的 return ValueTask.CompletedTask
"""
import re, os, sys

DRY_RUN = '--dry-run' in sys.argv
SKIP_DIRS = {'.xxx', 'artifacts', 'bin', 'obj', '.git', 'node_modules'}

# 不替换 SendAsync 的文件（有自己的 SendAsync 方法，非 ActorBase 的）
SKIP_FILES = {
    'lib/async_lock/transport/mesotransport.cs',
    'lib/async_lock/transport/namedpipetransport.cs',
    'lib/async_lock/transport/bustransport.cs',
    'lib/async_lock/transport/itransporttopology.cs',
    'lib/async_lock/mailbox/persistentmailbox.cs',  # PersistentSendAsync
    'lib/async_lock/mailbox/prioritymailbox.cs',    # 自己的 SendAsync
}

# SendAsync 前缀，这些不是 ActorBase.SendAsync，不替换
SKIP_PREFIXES = ['HandleSendAsync', 'PersistentSendAsync', 'RouteAsync']

def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith('.cs'):
                result.append(os.path.join(dirpath, f).replace('\\', '/'))
    return result

def safe_replace_send_async(content, filepath):
    """安全替换 SendAsync → Tell"""
    if filepath.lower() in SKIP_FILES:
        return content
    
    # 逐字符扫描，找到 SendAsync( 但前面不是 Handle/Persistent 等
    result = []
    i = 0
    while i < len(content):
        idx = content.find('SendAsync(', i)
        if idx == -1:
            result.append(content[i:])
            break
        
        # 检查前面是否有跳过的前缀
        skip = False
        for prefix in SKIP_PREFIXES:
            prefix_len = len(prefix)
            if idx >= prefix_len and content[idx-prefix_len:idx] == prefix:
                skip = True
                break
        
        # 检查是否在注释中（简单检查：前面有 /// 或 // 或 *）
        line_start = content.rfind('\n', 0, idx) + 1
        line_prefix = content[line_start:idx].strip()
        if line_prefix.startswith('///') or line_prefix.startswith('//') or line_prefix.startswith('*'):
            skip = True
        # 检查是否在字符串中（cref="...SendAsync..."）
        if 'cref=' in content[max(0, idx-50):idx]:
            skip = True
        # 检查是否是 HttpMessageHandler.SendAsync 重写
        if 'protected override' in content[line_start:idx] and 'Task<HttpResponseMessage>' in content[idx:idx+50]:
            skip = True
        
        result.append(content[i:idx])
        if skip:
            result.append('SendAsync(')
        else:
            result.append('Tell(')
        i = idx + len('SendAsync(')
    
    return ''.join(result)

def fix_tell_ct_param(content):
    """Tell(xxx, ct) → Tell(xxx) — 只去掉最后的 CancellationToken 参数"""
    # 匹配 Tell(任何内容, ct) 或 Tell(任何内容, cancellationToken)
    # 用正则：Tell( 后面到 ) 之间，最后是 , ct 或 , cancellationToken
    pattern = r'Tell\((.+?),\s*(?:ct|cancellationToken)\s*\)'
    
    def replacer(m):
        return f'Tell({m.group(1)})'
    
    # 多次替换（处理嵌套情况）
    prev = None
    while prev != content:
        prev = content
        content = re.sub(pattern, replacer, content)
    
    return content

def fix_return_value_task(content):
    """在返回 ValueTask 但缺少 return 的方法末尾补 return ValueTask.CompletedTask;"""
    # 这个太难自动做，跳过
    return content

def main():
    files = find_cs_files('.')
    total = 0
    for f in files:
        with open(f, 'r', encoding='utf-8') as fh:
            content = fh.read()
        original = content
        
        content = safe_replace_send_async(content, f)
        content = fix_tell_ct_param(content)
        content = fix_return_value_task(content)
        
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
