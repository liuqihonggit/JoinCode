#!/usr/bin/env python3
"""
功能域迁移脚本 — 将指定域的项目从原位置迁移到 modules/<domain>/

用法:
  python migrate_domain.py --domain persistence --projects lib/pithosdb --no-tests
  python migrate_domain.py --domain async_primitives --projects lib/async_lock lib/bcl_bridge lib/transport.contracts lib/transport.impl

功能:
  1. git mv 项目到 modules/<domain>/
  2. 更新所有 csproj 中引用旧路径的 ProjectReference
  3. 更新根目录 Directory.Build.props 中的全局引用
  4. 更新所有 slnx 文件中的项目路径
  5. 生成 module.slnx + README.md + CHANGELOG.md
  6. 可选: dotnet restore + build 验证
"""
import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent

def run(cmd, cwd=None):
    """执行命令，返回 (returncode, stdout)"""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True, cwd=cwd or REPO_ROOT)
    return result.returncode, result.stdout + result.stderr

def find_csproj_files():
    """找到所有 csproj 文件"""
    files = []
    for root, dirs, names in os.walk(REPO_ROOT):
        # 跳过 .xxx, artifacts, .git
        dirs[:] = [d for d in dirs if d not in {'.xxx', 'artifacts', '.git', '.codeartsdoer', '.nuget', '.jcc', '.coverage-tmp'}]
        for n in names:
            if n.endswith('.csproj'):
                files.append(Path(root) / n)
    return files

def find_slnx_files():
    """找到所有 slnx 文件"""
    return list(REPO_ROOT.glob('build/sln/*.slnx'))

def update_csproj_refs(old_path_prefix, new_path_prefix):
    """更新所有 csproj 中引用旧路径的 ProjectReference
    
    old_path_prefix: 如 'lib/structura'
    new_path_prefix: 如 'modules/collections/structura'
    """
    changed = []
    for csproj in find_csproj_files():
        text = csproj.read_text(encoding='utf-8')
        if f'$(RepoRoot){old_path_prefix}/' in text:
            new_text = text.replace(
                f'$(RepoRoot){old_path_prefix}/',
                f'$(RepoRoot){new_path_prefix}/'
            )
            csproj.write_text(new_text, encoding='utf-8')
            changed.append(str(csproj.relative_to(REPO_ROOT)))
    return changed

def update_slnx_refs(old_path_prefix, new_path_prefix):
    """更新所有 slnx 中的项目路径
    
    slnx 路径格式: ../../lib/structura/Structura.csproj
    """
    changed = []
    old_rel = f'../../{old_path_prefix}/'
    new_rel = f'../../{new_path_prefix}/'
    for slnx in find_slnx_files():
        text = slnx.read_text(encoding='utf-8')
        if old_rel in text:
            new_text = text.replace(old_rel, new_rel)
            slnx.write_text(new_text, encoding='utf-8')
            changed.append(str(slnx.relative_to(REPO_ROOT)))
    return changed

def update_build_props(old_path_prefix, new_path_prefix):
    """更新根目录 Directory.Build.props 中的全局引用"""
    props = REPO_ROOT / 'Directory.Build.props'
    text = props.read_text(encoding='utf-8')
    old = f'$(RepoRoot){old_path_prefix}/'
    new = f'$(RepoRoot){new_path_prefix}/'
    if old in text:
        new_text = text.replace(old, new)
        props.write_text(new_text, encoding='utf-8')
        return True
    return False

def git_mv(src, dst):
    """git mv 目录"""
    src_abs = REPO_ROOT / src
    dst_abs = REPO_ROOT / dst
    dst_abs.parent.mkdir(parents=True, exist_ok=True)
    rc, out = run(f'git mv "{src_abs}" "{dst_abs}"')
    if rc != 0:
        print(f'  ⚠ git mv failed: {src} -> {dst}: {out}')
        return False
    return True

def generate_module_slnx(domain, projects):
    """生成 module.slnx"""
    lines = ['<Solution>']
    for proj in projects:
        proj_name = proj.split('/')[-1]
        csproj_name = proj_name + '.csproj'
        if '.tests' in proj_name.lower() or '.tests' in proj_name:
            pass
        lines.append(f'  <Project Path="{proj_name}/{csproj_name}" />')
    lines.append('</Solution>')
    return '\n'.join(lines) + '\n'

def generate_readme(domain, description, projects):
    """生成 README.md"""
    proj_list = '\n'.join(f'| {p.split("/")[-1]} | |' for p in projects)
    return f'''# {domain} — {description}

> 📍 **导航**: [modules/](../README.md) › {domain}

## 项目

| 项目 | 说明 |
|------|------|
{proj_list}

## 用法

```bash
dotnet build modules/{domain}/module.slnx
dotnet pack modules/{domain}/module.slnx -o ./nuget-feed
```
'''

def generate_changelog(domain):
    """生成 CHANGELOG.md"""
    return f'''# CHANGELOG — {domain}

## [Unreleased]

## [1.0.0] — 2026-10-11 从单仓库迁移
- 迁移到 modules/{domain}/
- 完整 git 历史见主仓库 git log
'''

def migrate_domain(domain, projects, description='', skip_build=False):
    """迁移一个功能域"""
    print(f'\n=== 迁移 {domain} 域 ===')
    print(f'项目: {projects}')
    
    domain_dir = f'modules/{domain}'
    all_changed = []
    
    # 1. git mv 每个项目
    for proj in projects:
        proj_name = proj.split('/')[-1]
        old_prefix = proj  # 如 lib/pithosdb
        new_prefix = f'{domain_dir}/{proj_name}'
        
        print(f'  git mv {proj} -> {new_prefix}')
        if not git_mv(proj, new_prefix):
            print(f'  ⚠ 跳过 {proj}')
            continue
        
        # 2. 更新所有 csproj 中的引用
        changed = update_csproj_refs(old_prefix, new_prefix)
        all_changed.extend(changed)
        
        # 3. 更新 slnx 中的引用
        changed = update_slnx_refs(old_prefix, new_prefix)
        all_changed.extend(changed)
        
        # 4. 更新 Directory.Build.props
        if update_build_props(old_prefix, new_prefix):
            all_changed.append('Directory.Build.props')
    
    # 5. 更新域内 csproj 的引用（移到新位置后，内部引用旧路径的也要更新）
    for proj in projects:
        proj_name = proj.split('/')[-1]
        old_prefix = proj
        new_prefix = f'{domain_dir}/{proj_name}'
        # 域内 csproj 可能引用同域其他项目，路径需要更新
        csproj_path = REPO_ROOT / new_prefix / f'{proj_name}.csproj'
        if csproj_path.exists():
            text = csproj_path.read_text(encoding='utf-8')
            for other_proj in projects:
                other_name = other_proj.split('/')[-1]
                other_old = f'$(RepoRoot){other_proj}/'
                other_new = f'$(RepoRoot){domain_dir}/{other_name}/'
                if other_old in text:
                    text = text.replace(other_old, other_new)
            csproj_path.write_text(text, encoding='utf-8')
    
    # 6. 生成元文件
    (REPO_ROOT / domain_dir).mkdir(parents=True, exist_ok=True)
    
    module_slnx = REPO_ROOT / domain_dir / 'module.slnx'
    if not module_slnx.exists():
        module_slnx.write_text(generate_module_slnx(domain, projects), encoding='utf-8')
        print(f'  生成 module.slnx')
    
    readme = REPO_ROOT / domain_dir / 'README.md'
    if not readme.exists():
        readme.write_text(generate_readme(domain, description or domain, projects), encoding='utf-8')
        print(f'  生成 README.md')
    
    changelog = REPO_ROOT / domain_dir / 'CHANGELOG.md'
    if not changelog.exists():
        changelog.write_text(generate_changelog(domain), encoding='utf-8')
        print(f'  生成 CHANGELOG.md')
    
    print(f'  更新了 {len(all_changed)} 个引用文件')
    
    # 7. 编译验证
    if not skip_build:
        print('  restore...')
        rc, out = run('dotnet restore build/sln/JoinCode.slnx')
        if rc != 0:
            print(f'  ⚠ restore 失败: {out[-500:]}')
            return False
        
        print('  build...')
        rc, out = run('dotnet build build/sln/JoinCode.slnx --no-restore')
        if rc != 0:
            print(f'  ⚠ build 失败: {out[-1000:]}')
            return False
        print(f'  ✅ 编译通过')
    
    return True

def main():
    parser = argparse.ArgumentParser(description='功能域迁移脚本')
    parser.add_argument('--domain', required=True, help='域名称')
    parser.add_argument('--projects', nargs='+', required=True, help='项目路径(如 lib/pithosdb)')
    parser.add_argument('--description', default='', help='域描述')
    parser.add_argument('--skip-build', action='store_true', help='跳过编译验证')
    args = parser.parse_args()
    
    success = migrate_domain(args.domain, args.projects, args.description, args.skip_build)
    sys.exit(0 if success else 1)

if __name__ == '__main__':
    main()
