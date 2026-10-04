"""通用截图脚本 — 启动 exe，等待，前置窗口，截图。"""
import subprocess
import sys
import time
import os

def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"
    proc_name = sys.argv[2] if len(sys.argv) > 2 else "JoinCode.Gui"
    wait = int(sys.argv[3]) if len(sys.argv) > 3 else 20
    title_match = sys.argv[4] if len(sys.argv) > 4 else "JoinCode"

    # 杀旧进程
    subprocess.run(["powershell", "-Command", f"Stop-Process -Name '{proc_name}' -Force -ErrorAction SilentlyContinue"],
                   capture_output=True)
    time.sleep(2)

    # 启动
    subprocess.Popen([exe], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"Started {proc_name}, waiting {wait}s...")
    time.sleep(wait)

    # 用 pywinauto 找窗口并截图
    try:
        from pywinauto import Desktop
        desktop = Desktop(backend="uia")
        windows = desktop.windows()
        target = None
        for w in windows:
            try:
                title = w.window_text()
                if title_match in title and w.is_visible():
                    target = w
                    print(f"Found window: '{title}' rect={w.rectangle()}")
                    break
            except Exception:
                continue

        if target is None:
            # 回退：按进程名找
            for w in windows:
                try:
                    if proc_name.lower() in w.process_name().lower() and w.is_visible():
                        target = w
                        print(f"Found by process: '{w.window_text()}' rect={w.rectangle()}")
                        break
                except Exception:
                    continue

        if target is None:
            print("Window not found!")
            return

        # 前置 + 设焦点
        target.set_focus()
        time.sleep(1)
        target.restore()
        time.sleep(0.5)
        target.set_focus()
        time.sleep(1)

        # 截图
        rect = target.rectangle()
        ts = time.strftime("%Y%m%d_%H%M%S")
        out = rf"D:\project\w1\tool\{proc_name}_{ts}.png"
        target.capture_as_image().save(out)
        print(f"SCREENSHOT_PATH={out}")
        print(f"Size: {rect.width()}x{rect.height()}")
    except ImportError:
        print("pywinauto not available, trying PIL...")
        import PIL.ImageGrab as ig
        ts = time.strftime("%Y%m%d_%H%M%S")
        out = rf"D:\project\w1\tool\{proc_name}_{ts}.png"
        ig.grab().save(out)
        print(f"SCREENSHOT_PATH={out} (full screen)")

if __name__ == "__main__":
    main()
