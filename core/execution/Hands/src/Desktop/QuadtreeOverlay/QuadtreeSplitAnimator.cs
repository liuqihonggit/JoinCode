namespace JoinCode.Hands.Desktop.QuadtreeOverlay;

/// <summary>
/// 四叉树分裂动画器 — 从屏幕边界开始递归四等分,逐层展开,颜色从外到内淡化
/// 纯计算层,不依赖 GDI,可独立单元测试
/// </summary>
internal static class QuadtreeSplitAnimator
{
    /// <summary>计算第 depth 层的所有框坐标(第0层=1个框=屏幕边界)</summary>
    /// <param name="screenX">屏幕左上角X</param>
    /// <param name="screenY">屏幕左上角Y</param>
    /// <param name="screenW">屏幕宽度</param>
    /// <param name="screenH">屏幕高度</param>
    /// <param name="depth">分裂深度(0=外框,1=4框,2=16框...)</param>
    /// <returns>该层所有框的坐标列表</returns>
    public static List<QuadtreeRect> GetLayerRects(int screenX, int screenY, int screenW, int screenH, int depth)
    {
        if (depth == 0)
            return [new QuadtreeRect(screenX, screenY, screenW, screenH)];

        var parents = GetLayerRects(screenX, screenY, screenW, screenH, depth - 1);
        var result = new List<QuadtreeRect>(parents.Count * 4);
        foreach (var p in parents)
        {
            var halfW = p.Width / 2;
            var halfH = p.Height / 2;
            result.Add(new QuadtreeRect(p.X, p.Y, halfW, halfH));
            result.Add(new QuadtreeRect(p.X + halfW, p.Y, p.Width - halfW, halfH));
            result.Add(new QuadtreeRect(p.X, p.Y + halfH, halfW, p.Height - halfH));
            result.Add(new QuadtreeRect(p.X + halfW, p.Y + halfH, p.Width - halfW, p.Height - halfH));
        }
        return result;
    }

    /// <summary>颜色淡化:从 baseColor 向白色线性插值(depth=0最深,depth=maxDepth最浅)</summary>
    /// <param name="baseColor">Win32 COLORREF (0x00BBGGRR)</param>
    /// <param name="depth">当前层深度</param>
    /// <param name="maxDepth">最大层深度</param>
    /// <returns>淡化后的 COLORREF</returns>
    public static uint FadeColor(uint baseColor, int depth, int maxDepth)
    {
        if (maxDepth <= 0) return baseColor;
        var t = (double)depth / maxDepth;
        var r = baseColor & 0xFF;
        var g = (baseColor >> 8) & 0xFF;
        var b = (baseColor >> 16) & 0xFF;
        var nr = (uint)(r + (255 - r) * t);
        var ng = (uint)(g + (255 - g) * t);
        var nb = (uint)(b + (255 - b) * t);
        return nr | (ng << 8) | (nb << 16);
    }

    /// <summary>计算指定深度的线宽(外层粗内层细,最小1px)</summary>
    public static int GetPenWidth(int depth) => Math.Max(1, 5 - depth);
}

/// <summary>屏幕矩形坐标(X,Y=左上角,Width/Height=尺寸)</summary>
internal readonly record struct QuadtreeRect(int X, int Y, int Width, int Height);
