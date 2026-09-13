using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using IApplication app = Application.Create();
app.Init();

using Window window = new() { Title = "AOT Probe (Esc to quit)" };
Label label = new()
{
    Text = "Terminal.Gui AOT Probe - if you see this, AOT works!",
    X = Pos.Center(),
    Y = Pos.Center()
};
window.Add(label);

app.Run(window);
