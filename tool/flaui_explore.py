"""黑盒 UI 自动化探索脚本 — 用 pywinauto (UIAutomation API) 启动真实 GUI exe，
检查 Avalonia 控件是否暴露给 UIAutomation，并模拟点击 Activity Bar 按钮验证互斥行为。
"""
import subprocess
import sys
import time
from pywinauto import Application, Desktop
from pywinauto.findwindows import ElementNotFoundError

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def main():
    print(f"[1] 启动 GUI exe: {EXE_PATH}")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(3)

    print("[2] 查找主窗口...")
    try:
        win = app.window(title_re=".*JoinCode.*")
        win.wait('ready', timeout=10)
        print(f"    窗口标题: {win.window_text()}")
        print(f"    窗口矩形: {win.rectangle()}")
    except Exception as e:
        print(f"    [FAIL] 找不到主窗口: {e}")
        app.kill()
        return

    print("[3] 打印顶层控件树(深度3)...")
    try:
        win.print_control_identifiers(depth=3)
    except Exception as e:
        print(f"    [WARN] 打印控件树失败: {e}")

    print("[4] 查找 Activity Bar 按钮(按文本内容)...")
    buttons_found = {}
    for icon in ["💬", "📁", "📝", "🛡", "⚙", "🌙", "☀️"]:
        try:
            btn = win.child_window(title=icon)
            if btn.exists(timeout=1):
                buttons_found[icon] = btn
                print(f"    [OK] 找到按钮 {icon}: rect={btn.rectangle()}")
        except Exception:
            pass

    if not buttons_found:
        print("    [WARN] 未通过 title 找到按钮，尝试遍历所有 Button 控件...")
        try:
            all_btns = win.descendants(control_type="Button")
            print(f"    共找到 {len(all_btns)} 个 Button 控件")
            for i, b in enumerate(all_btns[:20]):
                name = b.window_text() or b.element_info.name or ""
                print(f"    Button[{i}]: name='{name}', rect={b.rectangle()}")
        except Exception as e:
            print(f"    [FAIL] 遍历 Button 失败: {e}")

    print("[5] 尝试点击 📁 目录树按钮...")
    if "📁" in buttons_found:
        try:
            btn = buttons_found["📁"]
            btn.click()
            time.sleep(1)
            print("    [OK] 点击 📁 成功")

            print("[6] 检查目录树面板是否出现...")
            try:
                tree = win.child_window(control_type="Tree")
                if tree.exists(timeout=2):
                    print(f"    [OK] 找到 TreeView 控件: rect={tree.rectangle()}")
                    items = tree.descendants(control_type="TreeItem")
                    print(f"    TreeView 有 {len(items)} 个 TreeItem")
                    for i, item in enumerate(items[:10]):
                        print(f"      Item[{i}]: {item.window_text()}")
                else:
                    print("    [FAIL] 未找到 TreeView 控件")
            except Exception as e:
                print(f"    [FAIL] 查找 TreeView 失败: {e}")
        except Exception as e:
            print(f"    [FAIL] 点击 📁 失败: {e}")
    else:
        print("    [SKIP] 未找到 📁 按钮")

    print("[7] 清理 — 关闭 GUI")
    try:
        app.kill()
    except Exception:
        pass
    print("[DONE] 黑盒探索完成")

if __name__ == "__main__":
    main()
