import os
import re
import glob

repo_root = r"D:\project\w1"

slnx_files = glob.glob(os.path.join(repo_root, "modules", "*", "module.slnx"))

fixed_count = 0
removed_count = 0

for slnx_path in slnx_files:
    slnx_dir = os.path.dirname(slnx_path)
    with open(slnx_path, 'r', encoding='utf-8') as f:
        content = f.read()

    lines = content.split('\n')
    new_lines = []
    changed = False

    for line in lines:
        m = re.match(r'\s*<Project Path="([^"]+)"\s*/>', line)
        if not m:
            new_lines.append(line)
            continue

        rel_path = m.group(1)
        full_path = os.path.join(slnx_dir, rel_path)

        if os.path.exists(full_path):
            new_lines.append(line)
            continue

        proj_dir = os.path.dirname(full_path)
        if os.path.isdir(proj_dir):
            csprojs = glob.glob(os.path.join(proj_dir, "*.csproj"))
            if len(csprojs) == 1:
                new_rel = os.path.relpath(csprojs[0], slnx_dir).replace('\\', '/')
                indent = re.match(r'(\s*)', line).group(1)
                new_lines.append(f'{indent}<Project Path="{new_rel}" />')
                changed = True
                fixed_count += 1
                print(f"  FIX: {os.path.basename(slnx_dir)}: {rel_path} -> {new_rel}")
                continue
            elif len(csprojs) == 0:
                indent = re.match(r'(\s*)', line).group(1)
                new_lines.append(f'{indent}<!-- REMOVED: {rel_path} (no .csproj found) -->')
                changed = True
                removed_count += 1
                print(f"  DEL: {os.path.basename(slnx_dir)}: {rel_path} (no csproj)")
                continue
            else:
                print(f"  SKIP: {os.path.basename(slnx_dir)}: {rel_path} -> multiple csprojs: {[os.path.basename(c) for c in csprojs]}")
                new_lines.append(line)
                continue
        else:
            indent = re.match(r'(\s*)', line).group(1)
            new_lines.append(f'{indent}<!-- REMOVED: {rel_path} (dir not found) -->')
            changed = True
            removed_count += 1
            print(f"  DEL: {os.path.basename(slnx_dir)}: {rel_path} (dir not found)")
            continue

    if changed:
        with open(slnx_path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(new_lines))
        print(f"  WROTE: {slnx_path}")

print(f"\nTotal: fixed={fixed_count}, removed={removed_count}")
