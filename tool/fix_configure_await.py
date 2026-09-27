import re, os, sys

SKIP_DIRS = {'.xxx', 'artifacts', 'bin', 'obj', '.git', 'node_modules'}
def find_cs_files(root='.'):
    result = []
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith('.cs'):
                result.append(os.path.join(dirpath, f).replace('\\', '/'))
    return result

total = 0
for f in find_cs_files():
    with open(f, 'r', encoding='utf-8') as fh:
        content = fh.read()
    original = content
    content = re.sub(r'\.ConfigureAwait\(false\)', '', content)
    if content != original:
        with open(f, 'w', encoding='utf-8') as fh:
            fh.write(content)
        total += 1
print(f'DONE: {total} files changed')
