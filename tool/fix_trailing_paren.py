"""Fix trailing ) → } in .cs files broken by batch scripts."""
import sys
from pathlib import Path

def fix_file(path: Path) -> bool:
    """Fix trailing ) → } in a single file. Returns True if changed."""
    try:
        content = path.read_text(encoding='utf-8-sig')
    except Exception as e:
        print(f"  SKIP {path}: {e}")
        return False

    lines = content.splitlines(keepends=True)
    if not lines:
        return False

    # Find last non-empty line
    last_idx = len(lines) - 1
    while last_idx >= 0 and lines[last_idx].strip() == '':
        last_idx -= 1

    if last_idx < 0:
        return False

    last_line = lines[last_idx]
    stripped = last_line.rstrip()
    if stripped == ')':
        # Replace ) with }
        indent = last_line[:len(last_line) - len(last_line.lstrip())]
        lines[last_idx] = indent + '}\n'
        path.write_text(''.join(lines), encoding='utf-8-sig')
        return True
    return False

def main():
    # Get list of modified .cs files from git
    import subprocess
    result = subprocess.run(['git', 'diff', '--name-only'], capture_output=True, text=True, cwd='D:/project/w2')
    files = [f.strip() for f in result.stdout.splitlines() if f.strip().endswith('.cs')]

    fixed = 0
    for f in files:
        path = Path('D:/project/w2') / f
        if fix_file(path):
            print(f"  FIXED {f}")
            fixed += 1

    print(f"\nTotal fixed: {fixed}")

if __name__ == '__main__':
    main()
