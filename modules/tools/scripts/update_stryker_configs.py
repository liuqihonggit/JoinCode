import os
import re

root = r"D:\project\w1"
old_slnx_names = ["Generators", "Foundation", "Infrastructure", "Core", "Services", "Composition", "App"]
pattern = re.compile(r'build/sln/(' + '|'.join(old_slnx_names) + r')\.slnx')

updated = []
for dirpath, _, filenames in os.walk(root):
    if '.git' in dirpath or '.xxx' in dirpath or 'node_modules' in dirpath:
        continue
    for fn in filenames:
        if fn == 'stryker-config.json':
            fp = os.path.join(dirpath, fn)
            with open(fp, 'r', encoding='utf-8') as f:
                content = f.read()
            new_content = pattern.sub('build/sln/JoinCode.slnx', content)
            if new_content != content:
                with open(fp, 'w', encoding='utf-8') as f:
                    f.write(new_content)
                updated.append(fp)

print(f"Updated {len(updated)} files:")
for fp in sorted(updated):
    print(f"  {fp}")
