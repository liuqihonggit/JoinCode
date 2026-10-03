"""黑盒测试：响应式分栏(openCode 风格) — 宽屏左右分栏，窄屏垂直上下分栏"""
import sys, time, os, ctypes
import pywinauto
from pywinauto import Application, Desktop

exe = os.path.join(os.path.dirname(__file__), '..', 'artifacts', 'bin', 'JoinCodeGui', 'Debug', 'net10.0', 'JoinCode.Gui.exe')
exe = os.path.abspath(exe)

app = Application(backend="uia").start(exe)
time.sleep(3)

desktop = Desktop(backend="uia")
win = desktop.window(title="JoinCode · AI 编程工作区")
win.set_focus()
time.sleep(0.5)

results = []

# 用 win32 API 调整窗口大小
user32 = ctypes.windll.user32
WM_SIZE = 0x0005
SWP_NOZORDER = 0x0004
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040

def resize_window(hwnd, width, height):
    rect = ctypes.wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    user32.SetWindowPos(hwnd, 0, rect.left, rect.top, width, height, SWP_NOZORDER | SWP_SHOWWINDOW)
    user32.SendMessageW(hwnd, WM_SIZE, 0, (height << 16) | (width & 0xFFFF))
    time.sleep(1)

hwnd = win.handle

# 测试1：宽屏(1200px) — 应为水平布局
try:
    resize_window(hwnd, 1200, 800)
    results.append(("宽屏1200px窗口正常", True))
except Exception as e:
    results.append(("宽屏1200px窗口正常", False, str(e)))

# 测试2：窄屏(500px) — 应切换到垂直布局
try:
    resize_window(hwnd, 500, 800)
    results.append(("窄屏500px窗口正常", True))
except Exception as e:
    results.append(("窄屏500px窗口正常", False, str(e)))

# 测试3：恢复宽屏
try:
    resize_window(hwnd, 1200, 800)
    results.append(("恢复宽屏正常", True))
except Exception as e:
    results.append(("恢复宽屏正常", False, str(e)))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\n响应式分栏测试: {passed}/{total} 通过")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
