"""验证：单击文件夹名称展开(VSCode 风格) + 双击文件打开编辑器。"""
import sys, time, os, subprocess, ctypes
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from pywinauto import Application, mouse

EXE = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def bring_to_front(win):
    win.set_focus()
    time.sleep(0.3)
    ctypes.windll.user32.SetForegroundWindow(win.handle)
    time.sleep(0.3)

def find_button(win, icon):
    for d in win.descendants():
        try:
            if icon in (d.window_text() or ""):
                return d
        except Exception:
            pass
    return None

def get_treeitem_text(item):
    parts = []
    try:
        for child in item.children():
            t = child.window_text() or ""
            if t:
                parts.append(t)
    except Exception:
        pass
    return " ".join(parts)

subprocess.run(["taskkill", "/im", "JoinCode.Gui.exe", "/f"], capture_output=True)
time.sleep(1)

app = Application(backend="uia").start(EXE)
time.sleep(4)
win = app.top_window()
bring_to_front(win)

btn = find_button(win, "📁")
btn.click_input()
time.sleep(1.5)
print("✅ 点击 📁 打开目录树")

tree_items = win.descendants(control_type="TreeItem")
print(f"  初始 TreeItem 数: {len(tree_items)}")

# 单击第一个文件夹(中心位置)
folder = tree_items[0]
rect = folder.rectangle()
name = get_treeitem_text(folder)
cx = (rect.left + rect.right) // 2
cy = (rect.top + rect.bottom) // 2
print(f"\n--- 单击文件夹: '{name}' 中心({cx},{cy}) ---")
mouse.click(coords=(cx, cy))
time.sleep(1.5)

tree_items_after = win.descendants(control_type="TreeItem")
print(f"  单击后 TreeItem 数: {len(tree_items_after)}")
if len(tree_items_after) > len(tree_items):
    added = len(tree_items_after) - len(tree_items)
    print(f"✅ 文件夹展开成功! 新增 {added} 个子节点")
    for item in tree_items_after[len(tree_items):len(tree_items)+5]:
        print(f"    子节点: '{get_treeitem_text(item)}'")
else:
    print("❌ 单击文件夹未展开")

# 再单击收起
mouse.click(coords=(cx, cy))
time.sleep(1)
tree_items_collapse = win.descendants(control_type="TreeItem")
if len(tree_items_collapse) < len(tree_items_after):
    print(f"✅ 再单击收起成功! TreeItem={len(tree_items_collapse)}")
else:
    print(f"⚠️ 再单击未收起 TreeItem={len(tree_items_collapse)}")

# 双击文件打开编辑器
all_items = win.descendants(control_type="TreeItem")
file_item = None
for item in all_items:
    txt = get_treeitem_text(item)
    if "." in txt and "📁" not in txt:
        file_item = item
        file_name = txt
        break

if file_item:
    fr = file_item.rectangle()
    fx = (fr.left + fr.right) // 2
    fy = (fr.top + fr.bottom) // 2
    print(f"\n--- 双击文件: '{file_name}' ({fx},{fy}) ---")
    mouse.double_click(coords=(fx, fy))
    time.sleep(2)
    edits = win.descendants(control_type="Edit")
    print(f"  Edit 控件: {len(edits)}")
    if edits:
        print("✅ 编辑器已打开!")

app.kill()
print("\n=== 验证结束 ===")
