"""完整黑盒 UI 自动化测试 — map[按钮,期望] 遍历 Activity Bar 每个按钮，
点击后验证 Side Bar 内容 + 主区内容 + 互斥状态。
用 pywinauto (UIAutomation API) 驱动真实 GUI exe。
"""
import time
import sys
from pywinauto import Application

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

# map[按钮, 期望]: 每个按钮点击后期望的面板内容
# 初始 ActiveSidePanel=Sessions, 点 💬=toggle收起, 点 📁=切到FileTree, 点 📝=切到Editor
BUTTON_EXPECTATIONS = {
    "💬": {
        "name": "Sessions(toggle收起)",
        "sidebar_markers": [],                        # 初始Sessions已激活,再点收起→Side Bar 空
        "sidebar_not_markers": ["目录树", "📂", "新建对话"],
        "treeview_count": 0,
    },
    "📁": {
        "name": "FileTree",
        "sidebar_markers": ["目录树", "📂"],         # FileTree 面板特征
        "sidebar_not_markers": ["新建对话"],
        "treeview_count": 1,
    },
    "📝": {
        "name": "Editor",
        "sidebar_markers": [],                       # Editor 时 Side Bar 收起
        "sidebar_not_markers": ["目录树", "新建对话"],
        "treeview_count": 0,
    },
}


def find_button(win, icon):
    """查找 Activity Bar 按钮"""
    try:
        btn = win.child_window(title=icon, control_type="Button")
        if btn.exists(timeout=1):
            return btn
    except Exception:
        pass
    return None


def get_sidebar_texts(win):
    """获取 Side Bar 区域所有 Text 控件文本"""
    texts = []
    try:
        for t in win.descendants(control_type="Text"):
            r = t.rectangle()
            # Side Bar 区域: x 在窗口左侧 48px~350px 范围内
            if r.left < 400 and r.width() > 0 and r.height() > 0:
                name = t.window_text() or ""
                if name:
                    texts.append(name)
    except Exception:
        pass
    return texts


def get_treeview_count(win):
    """获取可见 TreeView 数量"""
    count = 0
    try:
        for t in win.descendants(control_type="Tree"):
            r = t.rectangle()
            if r.width() > 0 and r.height() > 0:
                count += 1
    except Exception:
        pass
    return count


def check_markers(texts, markers, should_exist):
    """检查 markers 是否在 texts 中出现"""
    results = []
    for marker in markers:
        found = any(marker in t for t in texts)
        if should_exist:
            results.append((marker, found, found == should_exist))
        else:
            results.append((marker, found, found == should_exist))
    return results


def run_test(win, icon, expectation):
    """运行单个按钮测试"""
    name = expectation["name"]
    print(f"\n  --- 测试 {icon} ({name}) ---")

    btn = find_button(win, icon)
    if btn is None:
        return {"button": icon, "name": name, "result": "FAIL", "reason": "按钮未找到"}

    # 点击按钮
    try:
        btn.set_focus()
        time.sleep(0.3)
        btn.click_input()
        time.sleep(1.5)  # 等待 Side Bar 宽度动画完成
    except Exception as e:
        return {"button": icon, "name": name, "result": "FAIL", "reason": f"点击失败: {e}"}

    # 验证 Side Bar 内容
    sidebar_texts = get_sidebar_texts(win)
    print(f"    Side Bar 文本: {sidebar_texts[:10]}")

    # 检查期望出现的标记
    marker_results = check_markers(sidebar_texts, expectation["sidebar_markers"], True)
    # 检查不应出现的标记
    not_marker_results = check_markers(sidebar_texts, expectation["sidebar_not_markers"], False)

    all_markers_ok = all(r[2] for r in marker_results + not_marker_results)

    # 验证 TreeView 数量
    tree_count = get_treeview_count(win)
    tree_ok = tree_count == expectation["treeview_count"]
    print(f"    TreeView 数量: {tree_count} (期望 {expectation['treeview_count']})")

    # 输出标记检查结果
    for marker, found, ok in marker_results:
        status = "✓" if ok else "✗"
        print(f"    {status} 应有 '{marker}': {'找到' if found else '未找到'}")
    for marker, found, ok in not_marker_results:
        status = "✓" if ok else "✗"
        print(f"    {status} 不应有 '{marker}': {'找到' if found else '未找到'}")

    if all_markers_ok and tree_ok:
        return {"button": icon, "name": name, "result": "PASS", "reason": ""}
    else:
        reasons = []
        if not all_markers_ok:
            reasons.append("标记检查失败")
        if not tree_ok:
            reasons.append(f"TreeView 数量不符({tree_count}!={expectation['treeview_count']})")
        return {"button": icon, "name": name, "result": "FAIL", "reason": "; ".join(reasons)}


def main():
    print("=" * 60)
    print("Activity Bar 黑盒 UI 自动化测试")
    print("=" * 60)

    print(f"\n[1] 启动 GUI exe: {EXE_PATH}")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(3)

    win = app.window(title_re=".*JoinCode.*")
    win.wait('ready', timeout=10)
    print(f"    窗口: {win.window_text()} ({win.rectangle()})")

    print(f"\n[2] 遍历 {len(BUTTON_EXPECTATIONS)} 个按钮,执行 map[按钮,期望] 测试...")
    results = []
    for icon, expectation in BUTTON_EXPECTATIONS.items():
        result = run_test(win, icon, expectation)
        results.append(result)

    print("\n" + "=" * 60)
    print("测试结果汇总")
    print("=" * 60)
    print(f"{'按钮':<6} {'名称':<12} {'结果':<8} {'原因'}")
    print("-" * 50)
    passed = 0
    failed = 0
    for r in results:
        print(f"{r['button']:<6} {r['name']:<12} {r['result']:<8} {r['reason']}")
        if r['result'] == 'PASS':
            passed += 1
        else:
            failed += 1

    print(f"\n通过: {passed}/{len(results)}, 失败: {failed}/{len(results)}")
    print(f"总体: {'✓ ALL PASS' if failed == 0 else '✗ HAS FAILURES'}")

    print("\n[3] 互斥序列验证: 💬→📁→📝→💬")
    sequence_results = []
    for icon in ["💬", "📁", "📝", "💬"]:
        btn = find_button(win, icon)
        if btn:
            try:
                btn.set_focus()
                time.sleep(0.2)
                btn.click_input()
                time.sleep(0.5)
                sidebar_texts = get_sidebar_texts(win)
                tree_count = get_treeview_count(win)
                has_sessions = any("新建对话" in t for t in sidebar_texts)
                has_filetree = any("目录树" in t for t in sidebar_texts)
                print(f"    点 {icon} 后: Sessions={has_sessions}, FileTree={has_filetree}, Tree={tree_count}")
                sequence_results.append((icon, has_sessions, has_filetree))
            except Exception as e:
                print(f"    点 {icon} 失败: {e}")

    # 互斥验证: 任意时刻 Sessions 和 FileTree 不能同时为 True
    mutex_ok = all(not (s and f) for _, s, f in sequence_results)
    print(f"    互斥验证: {'✓ PASS' if mutex_ok else '✗ FAIL'}")

    print("\n[4] 清理")
    try:
        app.kill()
    except Exception:
        pass

    total_pass = passed + (1 if mutex_ok else 0)
    total = len(results) + 1
    print(f"\n最终结果: {total_pass}/{total} 通过")
    sys.exit(0 if total_pass == total else 1)


if __name__ == "__main__":
    main()
