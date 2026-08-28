using SkiaSharp;

var bmp = new SKBitmap(100, 100);
using var canvas = new SKCanvas(bmp);
canvas.Clear(SKColors.White);

using var paint = new SKPaint
{
    Color = SKColors.Red,
    Style = SKPaintStyle.Stroke,
    StrokeWidth = 2,
    PathEffect = SKPathEffect.CreateDash([5f, 5f], 0)
};

canvas.DrawRect(new SKRect(10, 10, 90, 90), paint);

using var img = SKImage.FromBitmap(bmp);
using var data = img.Encode(SKEncodedImageFormat.Png, 100);

Console.WriteLine($"SkiaSharp AOT OK. PNG bytes: {data.Size}");
