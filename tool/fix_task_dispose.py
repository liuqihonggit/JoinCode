import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
ROOT = Path(r'D:\project\w1')

files = [
    'test/mock/sync.integration.tests/ChatServiceTests.cs',
    'test/mock/sync.integration.tests/TranscriptPersistIntegrationTests.cs',
    'llm/agents.tests/Agents/Coordinator/ForkSubAgentManagerActorTests.cs',
    'test/unit/hands.tests/registry/LocalToolRegistryTest.cs',
    'test/unit/hands.tests/handlers/ToolCreationToolHandlersTest.cs',
]

sig_re = re.compile(r'(public|protected|internal|private)?\s*(override\s+|new\s+)?(async\s+)?Task\s+Dispose(Async)?\s*\(')

def find_methods(lines):
    results = []
    for i, ln in enumerate(lines):
        if sig_re.search(ln):
            for j in range(i, len(lines)):
                if '{' in lines[j]: bs = j; break
            else: continue
            depth = 0
            for j in range(bs, len(lines)):
                depth += lines[j].count('{') - lines[j].count('}')
                if depth == 0:
                    results.append((i, bs, j))
                    break
    return results

total = 0
for relpath in files:
    fpath = ROOT / relpath
    if not fpath.exists(): continue
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    changed = False

    for s, bs, e in find_methods(new):
        if 'async ' in new[s]:
            new[s] = new[s].replace('async ', '', 1); changed = True
        for i in range(bs, e):
            ln = new[i]; indent = len(ln) - len(ln.lstrip()); ind = ln[:indent]
            m = re.match(r'\s*await\s+(.+?)\.ConfigureAwait\((?:false|true)\);\s*$', ln)
            if not m: m = re.match(r'\s*await\s+(.+?);\s*$', ln)
            if m:
                new[i] = f'{ind}_ = {m.group(1).strip()};'; changed = True; continue
            if re.search(r'\breturn\s*;', ln) and 'Task' not in ln:
                new[i] = ln.replace('return;', 'return Task.CompletedTask;'); changed = True
        last_content = e - 1
        while last_content > bs and new[last_content].strip() == '': last_content -= 1
        if new[last_content].strip() and not new[last_content].strip().startswith('return'):
            if new[e].strip() == '}':
                new.insert(e, '        return Task.CompletedTask;'); changed = True

    if changed:
        diff = list(difflib.unified_diff(orig, new, fromfile=relpath, tofile=relpath, lineterm=''))
        if diff:
            print('\n'.join(diff)); print(); total += 1
            if not DRY: fpath.write_text('\n'.join(new) + '\n', encoding='utf-8')

mode = 'DRY-RUN' if DRY else 'APPLIED'
print(f'=== {mode}: {total} files ===')
