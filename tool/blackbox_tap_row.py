"""验证：点击 TreeItem 整行任意位置都能展开文件夹。"""
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
folder = tree_items[0]
rect = folder.rectangle()
cy = (rect.top + rect.bottom) // 2
initial = len(tree_items)
print(f"  文件夹 rect={rect}, 初始 TreeItem={initial}")

# 测试多个位置：左、中左、中、中右、右
positions = [
    ("最左", rect.left + 5),
    ("左1/4", rect.left + (rect.right - rect.left) // 4),
    ("中心", (rect.left + rect.right) // 2),
    ("右3/4", rect.left + (rect.right - rect.left) * 3 // 4),
    ("最右", rect.right - 5),
]

all_ok = True
for label, cx in positions:
    mouse.click(coords=(cx, cy))
    time.sleep(1)
    after = win.descendants(control_type="TreeItem")
    expanded = len(after) > initial
    print(f"  {label} ({cx},{cy}): {'✅展开' if expanded else '❌未展开'} TreeItem={len(after)}")
    if not expanded:
        all_ok = False
    # 收起
    if expanded:
        mouse.click(coords=(cx, cy))
        time.sleep(0.5)

if all_ok:
    print("\n✅ 点击整行任意位置都能展开!")
else:
    print("\n⚠️ 部分位置未展开")

app.kill()
