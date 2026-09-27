"""
修复 await xxx.Tell(...) → xxx.Tell(...) (Tell 返回 void 不能 await)
修复 MockHttpMessageHandler.Tell → SendAsync
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

def fix_await_tell(content):
    """await xxx.Tell(...) → xxx.Tell(...)  /  await Tell(...) → Tell(...)"""
    # await xxx.Tell(...).ConfigureAwait(false) → xxx.Tell(...)
    content = re.sub(r'await\s+(\w+)\.Tell\(', r'\1.Tell(', content)
    # await Tell(...).ConfigureAwait(false) → Tell(...)
    content = re.sub(r'await\s+Tell\(', r'Tell(', content)
    # 去掉 .ConfigureAwait(false) （Tell 返回 void，不能 ConfigureAwait）
    # 但只去掉 Tell(...).ConfigureAwait(false) 的
    # 这个需要更精确的匹配，暂不处理
    return content

def fix_mock_http(content):
    """MockHttpMessageHandler.Tell → SendAsync"""
    # protected override Task<HttpResponseMessage> Tell( → SendAsync(
    content = re.sub(
        r'protected override Task<HttpResponseMessage>\s+Tell\(',
        'protected override Task<HttpResponseMessage> SendAsync(',
        content
    )
    return content

def main():
    files = find_cs_files('.')
    total = 0
    for f in files:
        with open(f, 'r', encoding='utf-8') as fh:
            content = fh.read()
        original = content
        
        content = fix_await_tell(content)
        content = fix_mock_http(content)
        
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
