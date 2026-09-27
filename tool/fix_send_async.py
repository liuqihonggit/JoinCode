"""
修复脚本:
1. SendAsync → Tell (排除 transport 的 SendAsync)
2. 误删的 return ValueTask.CompletedTask 补回
"""
import re, os, sys

DRY_RUN = '--dry-run' in sys.argv
SKIP_DIRS = {'.xxx', 'artifacts', 'bin', 'obj', '.git', 'node_modules'}

# transport 文件不替换 SendAsync (它们有自己的 SendAsync 方法)
TRANSPORT_FILES = {
    'lib/async_lock/transport/MeshTransport.cs',
    'lib/async_lock/transport/NamedPipeTransport.cs',
    'lib/async_lock/transport/BusTransport.cs',
    'lib/async_lock/transport/ITransportTopology.cs',
}

def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith('.cs'):
                fp = os.path.join(dirpath, f).replace('\\', '/')
                result.append(fp)
    return result

def fix_send_async(content, filepath):
    """替换 ActorBase.SendAsync → Tell"""
    if filepath in TRANSPORT_FILES:
        return content, 0
    count = 0
    
    # await SendAsync(cmd, ct) → Tell(cmd)  (子类内部调用基类)
    new = re.sub(r'await\s+SendAsync\(([^,)]+)(?:,\s*[^)]+)?\)\s*\.ConfigureAwait\(false\)\s*;', r'Tell(\1);', content)
    count += len(content) - len(new) != 0
    content = new
    
    # await xxx.SendAsync(cmd, ct) → xxx.Tell(cmd)
    new = re.sub(r'await\s+(\w+)\.SendAsync\(([^,)]+)(?:,\s*[^)]+)?\)\s*\.ConfigureAwait\(false\)\s*;', r'\1.Tell(\2);', content)
    count += len(content) - len(new) != 0
    content = new
    
    # SendAsync(cmd) → Tell(cmd) (不带 await)
    new = re.sub(r'(?<!\.)SendAsync\(', 'Tell(', content)
    count += len(content) - len(new) != 0
    content = new
    
    # .SendAsync( → .Tell( (但排除 transport)
    new = re.sub(r'\.SendAsync\(', '.Tell(', content)
    count += len(content) - len(new) != 0
    content = new
    
    return content, count

def fix_returns(content):
    """在返回 ValueTask 但没有 return 的方法末尾补 return ValueTask.CompletedTask;"""
    # 这个太复杂，跳过。手动修复。
    return content, 0

def main():
    files = find_cs_files('.')
    total = 0
    for f in files:
        with open(f, 'r', encoding='utf-8') as fh:
            content = fh.read()
        original = content
        
        content, c1 = fix_send_async(content, f)
        content, c2 = fix_returns(content)
        
        if content != original:
            if DRY_RUN:
                print(f"[DRY-RUN] {f} (SendAsync fixes: {c1})")
            else:
                with open(f, 'w', encoding='utf-8') as fh:
                    fh.write(content)
                print(f"[DONE] {f}")
                total += 1
    mode = "DRY-RUN" if DRY_RUN else "DONE"
    print(f"\n{mode}: {total} files changed")

if __name__ == '__main__':
    main()
