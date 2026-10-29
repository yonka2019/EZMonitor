using System.Windows;

namespace EZMonitor;

public partial class App : Application
{
    EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest")) { Shutdown(SelfTest.Run()); return; }

        // Second launch just brings the running window forward
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "EZMonitor.Show", out bool first);
        if (!first) { _showSignal.Set(); Shutdown(); return; }

        var window = new MainWindow();
        if (!e.Args.Contains("--tray")) window.Open();
        if (Array.IndexOf(e.Args, "--snap") is var i and >= 0 && i + 1 < e.Args.Length) Snap(window, e.Args[i + 1]);
        new Thread(() => { while (_showSignal.WaitOne()) Dispatcher.Invoke(window.Open); }) { IsBackground = true }.Start();
    }

    /// <summary>--snap file.png: renders the window after 12 s of samples, then exits. For checking the layout.</summary>
    static async void Snap(Window window, string path)
    {
        await Task.Delay(12_000);
        var root = (FrameworkElement)window.Content;
        var size = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        var paper = new System.Windows.Media.DrawingVisual();
        using (var dc = paper.RenderOpen()) dc.DrawRectangle(window.Background, null, size);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(paper);
        bmp.Render(root);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var f = System.IO.File.Create(path)) png.Save(f);
        Environment.Exit(0);
    }
}
