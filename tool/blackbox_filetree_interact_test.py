"""黑盒测试: 目录树交互 — 双击展开目录/打开文件/右键菜单"""
import time
import os
import subprocess
from pywinauto import Application

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"


def find_button(win, icon):
    try:
        btn = win.child_window(title=icon, control_type="Button")
        if btn.exists(timeout=1):
            return btn
    except Exception:
        pass
    return None


def get_treeitems(win):
    """获取所有可见 TreeItem 及其文本(从子 Text 控件提取)"""
    items = []
    try:
        for t in win.descendants(control_type="TreeItem"):
            r = t.rectangle()
            if r.width() > 0 and r.height() > 0:
                # TreeItem 文本在子 Text 控件中
                text = t.window_text() or ""
                if not text:
                    # 从子控件提取文本
                    try:
                        children = t.children()
                        child_texts = []
                        for child in children:
                            ct = child.window_text() or ""
                            if ct:
                                child_texts.append(ct)
                        text = " ".join(child_texts)
                    except Exception:
                        pass
                items.append((t, text))
    except Exception:
        pass
    return items


def main():
    print("=" * 60)
    print("目录树交互黑盒测试")
    print("=" * 60)

    print(f"\n[1] 启动 GUI exe")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(5)
    win = app.window(title_re=".*JoinCode.*")
    win.wait('ready', timeout=10)
    print(f"    窗口: {win.window_text()}")

    print("\n[2] 点击 📁 打开目录树")
    btn = find_button(win, "📁")
    if not btn:
        # 重试查找
        time.sleep(3)
        btn = find_button(win, "📁")
    if not btn:
        print("    [FAIL] 📁 按钮未找到")
        app.kill()
        return
    btn.set_focus()
    time.sleep(0.3)
    btn.click_input()
    time.sleep(1.5)

    items = get_treeitems(win)
    print(f"    TreeView 有 {len(items)} 个可见 TreeItem")
    for i, (item, text) in enumerate(items[:15]):
        print(f"      [{i}] '{text}'")

    if not items:
        print("    [FAIL] 没有 TreeItem")
        app.kill()
        return

    print("\n[3] 双击第一个文件夹(展开/收起)")
    folder_item = None
    for item, text in items:
        if "📁" in text or "📂" in text:
            folder_item = item
            folder_text = text
            break

    if folder_item:
        print(f"    找到文件夹: {folder_text}")
        try:
            # 方式1: pywinauto expand() 方法 (UIAutomation ExpandCollapse pattern)
            expanded = False
            try:
                folder_item.expand()
                time.sleep(1)
                expanded = True
                print("    [方式1] expand() 成功")
            except Exception as e:
                print(f"    [方式1] expand() 失败: {e}")

            new_items = get_treeitems(win)
            print(f"    expand() 后 TreeItem 数量: {len(new_items)} (之前 {len(items)})")

            if len(new_items) <= len(items):
                # 方式2: 双击 TreeItem
                try:
                    folder_item.double_click_input()
                    time.sleep(1)
                    print("    [方式2] double_click_input()")
                except Exception as e:
                    print(f"    [方式2] 双击失败: {e}")
                new_items = get_treeitems(win)

            if len(new_items) <= len(items):
                # 方式3: 点击 TreeItem 左侧不同位置
                r = folder_item.rectangle()
                import ctypes
                for offset in [5, 10, 15, 20, 25, 30]:
                    x = r.left + offset
                    y = r.top + (r.bottom - r.top) // 2
                    ctypes.windll.user32.SetCursorPos(x, y)
                    ctypes.windll.user32.mouse_event(0x0002, 0, 0, 0, 0)
                    ctypes.windll.user32.mouse_event(0x0004, 0, 0, 0, 0)
                    time.sleep(0.5)
                    new_items = get_treeitems(win)
                    if len(new_items) > len(items):
                        print(f"    [方式3] 点击偏移 {offset}px 展开成功")
                        break

            new_items = get_treeitems(win)
            if len(new_items) > len(items):
                print(f"    [OK] 文件夹展开成功,新增 {len(new_items) - len(items)} 个子项")
                for i, (item, text) in enumerate(new_items[:30]):
                    print(f"      [{i}] '{text}'")
            else:
                print(f"    [FAIL] 所有方式都未展开(可能 IsExpanded 绑定问题)")
        except Exception as e:
            print(f"    [FAIL] 点击失败: {e}")
    else:
        print("    [WARN] 未找到文件夹 TreeItem")

    print("\n[4] 双击 .cs 文件(在编辑器中打开)")
    items = get_treeitems(win)
    cs_item = None
    cs_text = ""
    for item, text in items:
        if ".cs" in text or ".md" in text or ".json" in text:
            cs_item = item
            cs_text = text
            break

    if cs_item:
        print(f"    找到文件: {cs_text}")
        try:
            cs_item.double_click_input()
            time.sleep(1.5)

            # 检查编辑器是否出现
            print("    检查编辑器是否出现...")
            edits = win.descendants(control_type="Edit")
            customs = win.descendants(control_type="Custom")
            print(f"    Edit 控件: {len(edits)}, Custom 控件: {len(customs)}")

            # 检查是否有标签页出现
            texts = win.descendants(control_type="Text")
            tab_found = False
            for t in texts:
                name = t.window_text() or ""
                if cs_text in name or "📝" in name:
                    print(f"    [OK] 找到编辑器标签页: '{name}'")
                    tab_found = True
                    break
            if not tab_found:
                print(f"    [WARN] 未找到编辑器标签页 '{cs_text}'")
        except Exception as e:
            print(f"    [FAIL] 双击文件失败: {e}")
    else:
        print("    [WARN] 未找到 .cs/.md/.json 文件 TreeItem")

    print("\n[5] 右键文件 → 检查上下文菜单")
    items = get_treeitems(win)
    file_item = None
    for item, text in items:
        if ".cs" in text or ".md" in text or ".json" in text:
            file_item = item
            break

    if file_item:
        try:
            file_item.right_click_input()
            time.sleep(1)

            # 检查上下文菜单
            menus = win.descendants(control_type="Menu")
            menu_items = win.descendants(control_type="MenuItem")
            print(f"    右键后: Menu={len(menus)}, MenuItem={len(menu_items)}")
            for mi in menu_items[:10]:
                name = mi.window_text() or ""
                if name:
                    print(f"      菜单项: '{name}'")

            if len(menu_items) > 0:
                print("    [OK] 右键菜单出现")
                from pywinauto.keyboard import send_keys
                send_keys("{ESC}")
                time.sleep(0.5)
            else:
                print("    [WARN] 右键菜单未出现")
        except Exception as e:
            print(f"    [FAIL] 右键失败: {e}")
    else:
        print("    [WARN] 未找到文件 TreeItem")

    print("\n[6] 清理")
    try:
        app.kill()
    except Exception:
        pass
    print("[DONE]")


if __name__ == "__main__":
    main()
