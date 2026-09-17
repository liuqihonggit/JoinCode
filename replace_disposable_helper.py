#!/usr/bin/env python3
"""Replace DisposableHelper calls with inline Interlocked/Volatile."""
import re, sys, os, glob

DRY_RUN = '--dry-run' in sys.argv

files = set()
for pattern in ['lib/**/*.cs', 'kit/**/*.cs', 'server/**/*.cs', 'llm/**/*.cs', 'app/**/*.cs', 'test/**/*.cs']:
    for fpath in glob.glob(pattern, recursive=True):
        with open(fpath, 'r', encoding='utf-8-sig') as f:
            content = f.read()
        if 'DisposableHelper' in content and 'DisposableHelper.cs' not in fpath:
            files.add(fpath)

changes = []
for fpath in sorted(files):
    with open(fpath, 'r', encoding='utf-8-sig') as f:
        content = f.read()
    original = content

    # 1. Change bool fields used with DisposableHelper to int
    for field in ['_disposed', '_isDisposed']:
        content = re.sub(
            rf'private\s+bool\s+{field}\b',
            f'private int {field}',
            content
        )

    # 2. Replace TryMarkDisposed
    content = re.sub(
        r'DisposableHelper\.TryMarkDisposed\(ref\s+(_\w+)\)',
        r'Interlocked.Exchange(ref \1, 1) == 0',
        content
    )

    # 3. Replace IsDisposed
    content = re.sub(
        r'DisposableHelper\.IsDisposed\(ref\s+(_\w+)\)',
        r'Volatile.Read(ref \1) != 0',
        content
    )

    # 4. Replace ThrowIfDisposed
    content = re.sub(
        r'DisposableHelper\.ThrowIfDisposed\(ref\s+(_\w+),\s*([^)]+)\)',
        r'ObjectDisposedException.ThrowIf(Volatile.Read(ref \1) != 0, \2)',
        content
    )

    # 5. Remove DisposableHelper using (Core.Utils) if no other Core.Utils usage
    # Check if Core.Utils is still needed (other than DisposableHelper)
    remaining_core_utils = re.findall(r'Core\.Utils\.\w+', content)
    if not remaining_core_utils:
        content = re.sub(r'using\s+Core\.Utils;\s*\n', '', content)

    # 6. Ensure System.Threading is imported (for Interlocked/Volatile)
    if 'Interlocked.' in content or 'Volatile.' in content:
        if 'using System.Threading;' not in content:
            # Add after first using or at top
            lines = content.split('\n')
            insert_idx = 0
            for i, line in enumerate(lines):
                if line.startswith('using '):
                    insert_idx = i + 1
                elif line.startswith('namespace ') or line.startswith('['):
                    break
            lines.insert(insert_idx, 'using System.Threading;')
            content = '\n'.join(lines)

    if content != original:
        changes.append(fpath)
        if not DRY_RUN:
            with open(fpath, 'w', encoding='utf-8-sig') as f:
                f.write(content)

print(f"{'DRY RUN: ' if DRY_RUN else ''}{len(changes)} files to change:")
for f in changes:
    print(f"  {f}")
