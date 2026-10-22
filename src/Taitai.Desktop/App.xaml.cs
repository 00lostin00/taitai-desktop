using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Taitai.Desktop;
public partial class App : Application
{
    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\TaitaiDesktop", out var first);
        if (!first) { MessageBox.Show("苔苔已经在运行，可以在系统托盘中找她。", "苔苔"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(Path.Combine(AppSettings.DataDirectory, "errors.log"), DateTimeOffset.Now + " " + args.Exception.GetType().Name + ": " + args.Exception.Message + Environment.NewLine); } catch { }
            args.Handled = true;
        };
        MainWindow = new PetWindow();
        // Expose taskbar windows when inspecting the floating layout.
        if (Array.Exists(e.Args, value => value == "--inspect")) MainWindow.ShowInTaskbar = true;
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
