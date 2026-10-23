using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taitai.Desktop;

namespace Taitai.WindowsChecks;

public sealed class CheckApplication : Application
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    static bool IsTopmost(Window window) => (GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 8) != 0;
    static bool Above(Window higher, Window lower)
    {
        var target = new WindowInteropHelper(higher).Handle;
        for (var handle = GetWindow(new WindowInteropHelper(lower).Handle, 3); handle != IntPtr.Zero; handle = GetWindow(handle, 3))
            if (handle == target) return true;
        return false;
    }
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    [STAThread]
    public static int Main()
    {
        var app = new CheckApplication();
        app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/Taitai;component/AppResources.xaml") };
        return app.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            var settings = new AppSettings { SaveEnabled = false, LayerMode = "always", IdleInteraction = false, FullscreenAvoidance = false, EdgeTuck = false, GlobalHotkey = false, Scale = .75 };
            var pet = new PetWindow(settings, monitorServices: false); MainWindow = pet; pet.Show();
            await Task.Delay(300);
            var companions = Windows.OfType<Window>().Where(window => window != pet).ToArray();
            Check(companions.Length == 2, "both floating companions were created");
            var transforms = companions.Select(window => ((FrameworkElement)window.FindName("Root")).LayoutTransform).ToArray();
            var image = (Image)pet.FindName("PetImage"); var frameChanges = 0;
            DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image)).AddValueChanged(image, (_, _) => frameChanges++);
            var gaps = new List<double>(); var elapsed = Stopwatch.StartNew(); var previous = elapsed.Elapsed.TotalMilliseconds;
            var pulse = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(20) };
            pulse.Tick += (_, _) => { var now = elapsed.Elapsed.TotalMilliseconds; gaps.Add(now - previous); previous = now; };
            pulse.Start();
            var cover = new Window { Title = "苔苔 · 悬浮回归检查", Content = new TextBlock { Text = "正在检查窗口悬浮与动画，几秒后自动关闭。", FontSize = 24, Margin = new Thickness(40) }, WindowState = WindowState.Maximized };
            cover.Show();
            await Task.Delay(5500); pulse.Stop();
            var floating = companions.Prepend(pet).ToArray();
            Check(floating.All(IsTopmost), "idle pet and companions retain the native topmost flag");
            Check(floating.All(window => Above(window, cover)), "maximized ordinary windows remain below the pet group");
            Check(frameChanges >= 12, "idle animation keeps changing frames while another window is active");
            Check(companions.Select((window, index) => ReferenceEquals(((FrameworkElement)window.FindName("Root")).LayoutTransform, transforms[index])).All(value => value), "status refresh reuses companion transforms");
            var ordered = gaps.OrderBy(value => value).ToArray();
            var p95 = ordered[(int)((ordered.Length - 1) * .95)];
            Check(ordered.Length >= 80 && p95 < 100, "UI pulse remains responsive during status refresh");
            Console.WriteLine($"METRICS frames={frameChanges} pulses={gaps.Count} p95Ms={p95:0.0} maxMs={ordered[^1]:0.0}");
            settings.LayerMode = "normal"; pet.ApplySettings(); await Task.Delay(150);
            Check(floating.All(window => !IsTopmost(window)), "ordinary mode releases the whole group from topmost");
            settings.LayerMode = "always"; pet.ApplySettings(); await Task.Delay(150);
            Check(floating.All(IsTopmost), "restoring always-float restores the whole native group");
            var work = pet.CurrentWorkArea();
            pet.Top = work.Top + 40;
            var handle = (Button)pet.FindName("EdgeHandle");
            foreach (var side in new[] { "left", "right" })
            {
                pet.DockToEdge(side); await Task.Delay(150);
                Check(handle.IsVisible && !image.IsVisible && companions.All(window => !window.IsVisible), side + " docking replaces the pet and companions with a handle");
                var visibleWidth = side == "left" ? pet.Left + pet.Width - work.Left : work.Right - pet.Left;
                Check(Math.Abs(visibleWidth - handle.Width * settings.Scale) < 1, side + " handle stays inside the screen at the configured scale");
                var handleOrigin = pet.PointFromScreen(handle.PointToScreen(new Point(0, 0)));
                var expectedLeft = side == "left" ? work.Left : work.Right - handle.Width * settings.Scale;
                Check(Math.Abs(pet.Left + handleOrigin.X - expectedLeft) < 1, side + " rendered handle is aligned with the visible edge");
                Capture(handle, side);
                Check(settings.DockSide == side && settings.Left >= work.Left && settings.Left + pet.Width <= work.Right + 1, side + " docking saves the expanded position rather than the offscreen one");
                handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                Check(!handle.IsVisible && image.IsVisible && companions.All(window => window.IsVisible) && floating.All(IsTopmost), side + " handle restores the complete floating group");
            }
            pet.Left = work.Left + (work.Width - pet.Width) / 2; pet.SnapToScreenEdge();
            Check(settings.DockSide == "", "dragging away from the edge clears docking");
            settings.EdgeTuck = false; pet.Left = work.Left + 8; pet.SnapToScreenEdge();
            Check(settings.DockSide == "" && Math.Abs(pet.Left - work.Left - 8) < 1, "disabled docking leaves the dropped position alone");
            settings.EdgeTuck = true; pet.SnapToScreenEdge(); pet.Touch();
            Check(settings.DockSide == "left" && Math.Abs(pet.Left - work.Left) < 1, "near-edge drops snap to the edge");
            await Task.Delay(4300);
            Check(handle.IsVisible, "an idle docked pet automatically tucks after three seconds");
            settings.EdgeTuck = false; pet.ApplySettings(); await Task.Delay(100);
            Check(!handle.IsVisible && image.IsVisible && settings.DockSide == "", "disabling docking restores the pet and clears its saved side");
            Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL " + error.Message); Shutdown(1);
        }
    }

    static void Capture(FrameworkElement handle, string side)
    {
        var directory = Environment.GetEnvironmentVariable("TAITAI_CHECK_CAPTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(handle.ActualWidth * 3), (int)Math.Ceiling(handle.ActualHeight * 3), 288, 288, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(handle), null, new Rect(0, 0, handle.ActualWidth, handle.ActualHeight));
        image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, "edge-" + side + ".png")); encoder.Save(output);
    }
}
