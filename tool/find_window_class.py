"""查找 Avalonia 窗口的正确类名"""
import sys, time, os
import pywinauto
from pywinauto import Application, Desktop

exe = os.path.join(os.path.dirname(__file__), '..', 'artifacts', 'bin', 'JoinCodeGui', 'Debug', 'net10.0', 'JoinCode.Gui.exe')
exe = os.path.abspath(exe)

app = Application(backend="uia").start(exe)
time.sleep(3)

desktop = Desktop(backend="uia")
wins = desktop.windows()
for w in wins:
    print(f"类名: {w.class_name}, 标题: {w.window_text()}, 可见: {w.is_visible()}")

app.kill()
