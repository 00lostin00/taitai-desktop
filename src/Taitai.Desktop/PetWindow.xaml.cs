using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Taitai.Core;

namespace Taitai.Desktop;
public partial class PetWindow : Window
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public QuotaSnapshot? Quota { get; private set; }
    public string? QuotaError { get; private set; }
    public bool Refreshing { get; private set; }
    public ActivitySnapshot Activity { get; private set; } = new(PetActivity.Idle, 0, default, true, "等待 Codex 会话");
    public event Action? DataChanged;
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim polling = new(1, 1);
    readonly ActivityWatcher watcher;
    readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(70) };
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly BitmapSource[][] frames = new BitmapSource[11][];
    readonly Forms.NotifyIcon tray;
    readonly IdleInteractions interactions = new();
    GlassBarWindow? glass;
    ChatStatusWindow? chatStatus;
    public ChatLink? CurrentChat => ChatLink.Parse(Settings.ChatUrl) ?? ChatLink.Local(Activity.ThreadId);
    public string ChatHeading => (!string.IsNullOrWhiteSpace(Settings.ChatUrl) ? Settings.ChatTitle : null) ?? Activity.Title ?? "当前聊天";
    public string ChatStatusLine => Activity.IsStale ? Activity.Label : Activity.State switch
    {
        PetActivity.Tool => "正在调用 " + ShortToolName(Activity.ToolName),
        PetActivity.Thinking when Activity.ToolName is not null => "正在思考 · 已调用 " + ShortToolName(Activity.ToolName),
        PetActivity.Waiting => "等你回答或确认",
        PetActivity.Complete => "这一轮已完成",
        PetActivity.Failed => "本轮中断或出错",
        _ => Activity.Label
    };
    public IReadOnlyList<ActivitySnapshot> RecentChats => watcher.Recent;
    DateTimeOffset nextIdle = DateTimeOffset.UtcNow.AddSeconds(90), speechUntil, lookUntil;
    int lookDirection, shakeCount, dragDirection;
    double priorDragLeft;
    DateTimeOffset priorDragTime;
    bool manualHidden, fullscreenHidden, hotkeyRegistered, fullscreenOverride;
    public string HotkeyStatus { get; private set; } = "";
    public string StartupStatus { get; private set; } = "";
    bool SavingPower => Settings.BatterySaver && Forms.SystemInformation.PowerStatus.PowerLineStatus == Forms.PowerLineStatus.Offline;
    public string PluginStatus => $"{interactions.PackCount} 个互动包 · {interactions.EntryCount} 条台词" + (interactions.IgnoredFiles > 0 ? $" · 已跳过 {interactions.IgnoredFiles} 个无效文件" : "");
    public string PluginDirectory => Path.Combine(AppContext.BaseDirectory, "Plugins");
    DetailWindow? details;
    DateTimeOffset nextQuota = DateTimeOffset.MinValue;
    DateTimeOffset lastInteraction = DateTimeOffset.UtcNow;
    DateTimeOffset lastActivityPoll;
    DateTimeOffset lastFrame;
    DateTimeOffset gestureUntil;
    DateTimeOffset manualTuckUntil;
    int gestureRow = -1;
    int currentRow = -1, currentFrame;
    bool dragging, tucked, closing, activityInFlight;
    string dockSide = "";
    double expandedLeft;
    int failures;
    readonly HashSet<string> expiredRefreshes = new();
    static readonly int[][] Durations = {
        new[]{280,110,110,140,140,320}, new[]{120,120,120,120,120,120,120,220}, new[]{120,120,120,120,120,120,120,220},
        new[]{140,140,140,280}, new[]{140,140,140,140,280}, new[]{140,140,140,140,140,140,140,240},
        new[]{150,150,150,150,150,260}, new[]{120,120,120,120,120,220}, new[]{150,150,150,150,150,280}
    };

    public PetWindow()
    {
        InitializeComponent();
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        watcher = new(Path.Combine(string.IsNullOrWhiteSpace(codexHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : codexHome, "sessions"));
        var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "spritesheet.png")); sheet.EndInit(); sheet.Freeze();
        for (var row = 0; row < 11; row++)
        {
            var count = row < 9 ? Durations[row].Length : 8;
            frames[row] = new BitmapSource[count];
            for (var col = 0; col < count; col++) { var image = new CroppedBitmap(sheet, new Int32Rect(col * 192, row * 208, 192, 208)); image.Freeze(); frames[row][col] = image; }
        }
        PetImage.Source = frames[0][0];
        tray = new Forms.NotifyIcon { Text = "苔苔 · Codex 桌宠", Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "taitai.ico")), Visible = true };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示苔苔", null, (_, _) => Dispatcher.Invoke(() => Restore(true)));
        trayMenu.Items.Add("查看额度", null, (_, _) => Dispatcher.Invoke(ShowDetails));
        trayMenu.Items.Add("返回 ChatGPT 聊天", null, (_, _) => Dispatcher.Invoke(OpenCurrentChat));
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => Restore(true));
        ContextMenu = BuildMenu();
        MouseEnter += (_, _) => { lastInteraction = DateTimeOffset.UtcNow; Expand(); };
        MouseMove += (_, _) => lastInteraction = DateTimeOffset.UtcNow;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { details?.Close(); e.Handled = true; } if (e.Key == Key.Enter) ShowDetails(); };
        Loaded += async (_, _) =>
        {
            ReloadInteractions();
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle).AddHook(WindowMessage);
            ApplySettings();
            var work = WorkArea();
            Left = Settings.Left ?? work.Right - Width - 32; Top = Settings.Top ?? work.Bottom - Height - 24;
            ClampToScreen();
            glass = new GlassBarWindow(this); glass.Show(); glass.Sync();
            chatStatus = new ChatStatusWindow(this); SyncGlassVisibility();
            animation.Tick += Animate; animation.Start();
            clock.Tick += ClockTick; clock.Start();
            await RefreshQuotaAsync();
        };
        Closing += OnClosing;
        LocationChanged += (_, _) =>
        {
            glass?.Sync();
            chatStatus?.Sync();
            if (dragging) { var delta = Left - priorDragLeft; var now = DateTimeOffset.UtcNow; if (Math.Abs(delta) > 12) { var direction = Math.Sign(delta); if (direction != dragDirection && (now - priorDragTime).TotalMilliseconds < 650) shakeCount++; dragDirection = direction; priorDragTime = now; priorDragLeft = Left; } }
        };
        IsVisibleChanged += (_, _) => SyncGlassVisibility();
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("查看额度", ShowDetails);
        Item("返回当前 ChatGPT 聊天", OpenCurrentChat);
        Item("关联聊天与状态…", ShowChatSettings);
        Item("刷新额度", () => _ = RefreshQuotaAsync());
        Item("挥挥手", () => Gesture(3));
        Item("跳一下", () => Gesture(4));
        Item("随便聊一句", () => RandomInteraction(true));
        Item("召唤彩蛋", () => { Say("看好了，毛线弹簧启动！"); Gesture(4, 2); });
        Item("设置…", ShowDetails);
        menu.Items.Add(new Separator());
        Item("收起到最近的屏幕边缘", () => { var w = WorkArea(); dockSide = Left + Width / 2 < w.Left + w.Width / 2 ? "left" : "right"; manualTuckUntil = DateTimeOffset.UtcNow.AddMinutes(1); Tuck(); });
        Item("暂时隐藏（Ctrl+Alt+P 恢复）", HideManually);
        Item("退出苔苔", Close);
        menu.Opened += (_, _) => { lastInteraction = DateTimeOffset.UtcNow; Expand(); };
        return menu;
    }

    public void ApplySettings()
    {
        UpdateLayer();
        var scale = Math.Clamp(Settings.Scale, .75, 1.4);
        CanvasRoot.LayoutTransform = new ScaleTransform(scale, scale);
        Width = 320 * scale; Height = 308 * scale;
        NameText.Text = Settings.PetName; NameText.Visibility = Settings.ShowName && !tucked ? Visibility.Visible : Visibility.Collapsed;
        ConfigureHotkey(); glass?.Sync(); chatStatus?.Sync(); SyncGlassVisibility();
        if (!Settings.EdgeTuck) Expand();
        if (IsLoaded && !tucked) ClampToScreen();
        Settings.Save();
    }

    public async Task RefreshQuotaAsync()
    {
        if (!await polling.WaitAsync(0)) return;
        try
        {
            Refreshing = true; DataChanged?.Invoke();
            var value = await new CodexQuotaClient().ReadAsync(Settings.CodexBinary, lifetime.Token);
            if (closing) return;
            if (Settings.ResetNotice && Quota is not null)
                foreach (var window in value.Windows)
                {
                    var previous = Quota.Windows.FirstOrDefault(w => w.Bucket == window.Bucket && w.Label == window.Label);
                    if (previous?.ResetAt is not null && previous.ResetAt <= DateTimeOffset.UtcNow && window.ResetAt > previous.ResetAt && window.Remaining > previous.Remaining)
                        tray.ShowBalloonTip(5000, "苔苔 · 额度已更新", $"{window.DurationLabel}额度已同步，当前剩余 {window.Remaining:0}%。", Forms.ToolTipIcon.Info);
                }
            Quota = value; QuotaError = null; failures = 0; nextQuota = DateTimeOffset.UtcNow.AddMinutes(SavingPower ? 5 : 2);
            foreach (var window in value.Windows.Where(w => w.Remaining <= 20))
            {
                if (!Settings.LowQuotaNotice || Settings.NotifiedWindows.Contains(window.NotificationKey)) continue;
                tray.ShowBalloonTip(5000, "苔苔 · 额度提醒", $"{window.Bucket} {window.DurationLabel}剩余 {window.Remaining:0}%。{QuotaParser.Countdown(window.ResetAt, DateTimeOffset.UtcNow)}。", Forms.ToolTipIcon.Info);
                Settings.NotifiedWindows.Add(window.NotificationKey);
            }
            if (Settings.NotifiedWindows.Count > 40) Settings.NotifiedWindows = Settings.NotifiedWindows.TakeLast(20).ToHashSet();
            Settings.Save();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            QuotaError = ex is IOException or TimeoutException ? ex.Message : "额度连接暂不可用，请稍后重试。";
            failures++; nextQuota = DateTimeOffset.UtcNow.AddSeconds(Math.Min(600, 30 * Math.Pow(2, Math.Min(failures, 4))));
        }
        finally
        {
            Refreshing = false; polling.Release(); if (!closing) { UpdateCompact(); DataChanged?.Invoke(); }
        }
    }

    async void ClockTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        animation.Interval = TimeSpan.FromMilliseconds(SavingPower ? 140 : 70);
        if ((now - lastActivityPoll).TotalSeconds >= (SavingPower ? 4 : 2) && !activityInFlight)
        {
            lastActivityPoll = now; activityInFlight = true; var prior = Activity.State;
            var preferred = ChatLink.Parse(Settings.ChatUrl);
            try
            {
                var observed = await Task.Run(() => watcher.Poll(now, preferred?.ThreadId), lifetime.Token);
                Activity = preferred is { IsLocal: false } ? new(PetActivity.Idle, 0, default, true, "未接入聊天状态") { ThreadId = preferred.ThreadId, Title = Settings.ChatTitle } : observed;
            }
            catch (OperationCanceledException) { return; }
            catch { Activity = new(PetActivity.Idle, 0, default, true, "会话暂不可读"); }
            finally { activityInFlight = false; }
            if (closing) return;
            if (preferred is not null && Activity.ThreadId is null) Activity = Activity with { ThreadId = preferred.ThreadId, Title = Settings.ChatTitle };
            if (prior != Activity.State) lastInteraction = now;
            if (now >= manualTuckUntil && Activity.State is PetActivity.Thinking or PetActivity.Tool or PetActivity.Waiting) Expand();
        }
        if (now >= nextQuota && !Refreshing) _ = RefreshQuotaAsync();
        if (Quota is not null)
            foreach (var w in Quota.Windows)
                if (w.ResetAt is not null && now >= w.ResetAt && expiredRefreshes.Add(w.NotificationKey) && !Refreshing) _ = RefreshQuotaAsync();
        UpdateCompact(); DataChanged?.Invoke();
        UpdateLayer();
        var rawFullscreen = Settings.FullscreenAvoidance && NativeServices.FullscreenOnPetMonitor(this);
        if (!rawFullscreen) fullscreenOverride = false;
        var fullscreen = rawFullscreen && !fullscreenOverride;
        if (fullscreen && !fullscreenHidden && !manualHidden) { fullscreenHidden = true; details?.Close(); Hide(); }
        else if (!fullscreen && fullscreenHidden) { fullscreenHidden = false; if (!manualHidden) Restore(false); }
        if (now >= speechUntil) SpeechBubble.Visibility = Visibility.Collapsed;
        if (Settings.IdleInteraction && IsVisible && !tucked && !dragging && !IsMouseOver && glass?.IsMouseOver != true && details is null && !ContextMenu.IsOpen && Activity.State == PetActivity.Idle && (now - lastInteraction).TotalSeconds > 45 && now >= nextIdle)
            RandomInteraction(false);
        if (Settings.EdgeTuck && !dragging && !IsMouseOver && details is null && !ContextMenu.IsOpen && Activity.State is PetActivity.Idle or PetActivity.Complete && (now - lastInteraction).TotalSeconds > 10)
        {
            var work = WorkArea();
            if (Math.Abs(Left - work.Left) < 25) dockSide = "left";
            if (Math.Abs(work.Right - (Left + Width)) < 25) dockSide = "right";
            if (dockSide.Length > 0) Tuck();
        }
    }

    void UpdateCompact()
    {
        glass?.Update();
        chatStatus?.Update();
        var text = Settings.PetName + " · " + Activity.Label + " · " + (Quota is null ? "额度未连接" : string.Join(" · ", Quota.Windows.Take(2).Select(w => $"{w.DurationLabel}剩余{w.Remaining:0}%")));
        tray.Text = text[..Math.Min(63, text.Length)];
    }

    void Animate(object? sender, EventArgs e)
    {
        if (!IsVisible || closing) return;
        var now = DateTimeOffset.UtcNow;
        if (lookUntil > now && !Settings.ReduceMotion) { PetImage.Source = frames[9 + lookDirection / 8][lookDirection % 8]; return; }
        var row = Settings.ReduceMotion ? 0 : dragging ? (Forms.Cursor.Position.X < lastDragX ? 2 : 1) : gestureUntil > now ? gestureRow : Activity.State switch
        {
            PetActivity.Thinking or PetActivity.Tool => 7,
            PetActivity.Waiting => 6,
            PetActivity.Complete => 8,
            PetActivity.Failed => 5,
            _ => 0
        };
        if (row == 0 && Settings.FollowPointer && !Settings.ReduceMotion)
        {
            var point = PointFromScreen(new Point(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
            var dx = point.X - Width / 2; var dy = point.Y - 141 * Settings.Scale;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > 42 * Settings.Scale && distance < 800 * Settings.Scale)
            {
                var degrees = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
                var direction = (int)Math.Round(degrees / 22.5) % 16;
                PetImage.Source = frames[9 + direction / 8][direction % 8]; return;
            }
        }
        if (currentRow != row) { currentRow = row; currentFrame = 0; lastFrame = now; }
        if (!Settings.ReduceMotion && (now - lastFrame).TotalMilliseconds >= Durations[row][currentFrame]) { currentFrame = (currentFrame + 1) % frames[row].Length; lastFrame = now; }
        PetImage.Source = frames[row][Settings.ReduceMotion ? 0 : currentFrame];
    }

    int lastDragX;
    void PetMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Gesture(4); e.Handled = true; return; }
        Expand(); lastInteraction = DateTimeOffset.UtcNow;
        var before = new Point(Left, Top); lastDragX = Forms.Cursor.Position.X; priorDragLeft = Left; priorDragTime = DateTimeOffset.UtcNow; shakeCount = 0; dragDirection = 0; dragging = true;
        try { DragMove(); } catch (InvalidOperationException) { } finally { dragging = false; }
        if (Math.Abs(before.X - Left) + Math.Abs(before.Y - Top) < 4) { Gesture(3); Say("嗯，摸摸头就不生气啦。"); }
        else
        {
            ClampToScreen(); var work = WorkArea(); dockSide = "";
            if (Left - work.Left < 25) { Left = work.Left; dockSide = "left"; }
            else if (work.Right - (Left + Width) < 25) { Left = work.Right - Width; dockSide = "right"; }
            Settings.Left = Left; Settings.Top = Top; Settings.Save();
            if (shakeCount >= 2) { Gesture(shakeCount >= 5 ? 5 : 6); Say(shakeCount >= 5 ? "哼，头发都晃成毛线团了！" : "转晕啦……让我站稳一下。"); }
        }
    }
    public void Gesture(int row, int repeats = 1) { lookUntil = default; gestureRow = row; gestureUntil = DateTimeOffset.UtcNow.AddMilliseconds(Durations[row].Sum() * repeats + 150); currentRow = -1; lastInteraction = DateTimeOffset.UtcNow; }
    void ToggleDetails(object sender, RoutedEventArgs e) { if (details is null) ShowDetails(); else details.Close(); }
    public void ShowDetails()
    {
        Expand(); lastInteraction = DateTimeOffset.UtcNow;
        if (details is not null) { details.Activate(); return; }
        details = new DetailWindow(this) { Owner = this, Topmost = Topmost, MaxHeight = Math.Max(260, WorkArea().Height - 16) };
        details.Closed += (_, _) => { details = null; lastInteraction = DateTimeOffset.UtcNow; };
        details.Loaded += (_, _) => PositionDetails(); details.Show();
    }
    void PositionDetails()
    {
        if (details is null) return; var work = WorkArea();
        details.Left = Left - details.ActualWidth - 12 >= work.Left ? Left - details.ActualWidth - 12 : Math.Min(work.Right - details.ActualWidth, Left + Width + 12);
        details.Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - details.ActualHeight));
    }
    void Tuck()
    {
        if (tucked) return; expandedLeft = Left; tucked = true; SyncGlassVisibility(); SpeechBubble.Visibility = Visibility.Collapsed; NameText.Visibility = Visibility.Collapsed;
        var work = WorkArea(); Left = dockSide == "left" ? work.Left - Width / 2 + 16 : work.Right - Width / 2 - 16;
    }
    void Expand()
    {
        if (!tucked) return; tucked = false; Left = expandedLeft; NameText.Visibility = Settings.ShowName ? Visibility.Visible : Visibility.Collapsed; ClampToScreen(); SyncGlassVisibility();
    }
    Rect WorkArea()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var screen = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new(screen.Left / dpi.DpiScaleX, screen.Top / dpi.DpiScaleY, screen.Width / dpi.DpiScaleX, screen.Height / dpi.DpiScaleY);
    }
    void ClampToScreen() { var w = WorkArea(); Left = Math.Clamp(Left, w.Left, Math.Max(w.Left, w.Right - Width)); Top = Math.Clamp(Top, w.Top, Math.Max(w.Top, w.Bottom - Height)); }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        closing = true; lifetime.Cancel(); animation.Stop(); clock.Stop(); details?.Close(); glass?.Close(); chatStatus?.Close();
        NativeServices.UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, 8271);
        Settings.Left = tucked ? expandedLeft : Left; Settings.Top = Top; Settings.Save(); tray.Visible = false; tray.Dispose();
        Application.Current.Shutdown();
    }
    public static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    public void Touch() { lastInteraction = DateTimeOffset.UtcNow; Expand(); }
    void SyncGlassVisibility()
    {
        if (closing) return;
        if (glass is not null) { if (IsVisible && !tucked) { if (!glass.IsVisible) glass.Show(); glass.Sync(); } else glass.Hide(); }
        if (chatStatus is not null) { if (IsVisible && !tucked && Settings.ShowChatStatus) { if (!chatStatus.IsVisible) chatStatus.Show(); chatStatus.Update(); } else chatStatus.Hide(); }
    }
    void HideManually() { manualHidden = true; details?.Close(); Hide(); }
    void Restore(bool explicitRequest)
    {
        if (explicitRequest) { manualHidden = false; fullscreenHidden = false; fullscreenOverride = NativeServices.FullscreenOnPetMonitor(this); var work = WorkArea(); if (Left < work.Left - Width || Left > work.Right || Top < work.Top || Top > work.Bottom) { Left = work.Right - Width - 24; Top = work.Bottom - Height - 24; } }
        Show(); Expand(); ClampToScreen(); SyncGlassVisibility(); Touch();
    }
    void UpdateLayer()
    {
        var value = Settings.LayerMode == "always" || (Settings.LayerMode == "follow" && (Activity.State is PetActivity.Thinking or PetActivity.Tool or PetActivity.Waiting or PetActivity.Complete or PetActivity.Failed || DateTimeOffset.UtcNow < speechUntil));
        if (Topmost != value) Topmost = value; if (details is not null) details.Topmost = value; if (glass is not null) glass.Topmost = value; if (chatStatus is not null) chatStatus.Topmost = value;
    }
    void ConfigureHotkey()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        if (hotkeyRegistered && !Settings.GlobalHotkey) { NativeServices.UnregisterHotKey(handle, 8271); hotkeyRegistered = false; }
        if (Settings.GlobalHotkey && !hotkeyRegistered) hotkeyRegistered = NativeServices.RegisterHotKey(handle, 8271, 0x4003, 0x50);
        HotkeyStatus = !Settings.GlobalHotkey ? "快捷键已关闭" : hotkeyRegistered ? "Ctrl+Alt+P · 隐藏 / 显示" : "Ctrl+Alt+P 已被占用，可通过托盘恢复";
    }
    IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 8271) { if (IsVisible) HideManually(); else Restore(true); handled = true; }
        return IntPtr.Zero;
    }
    public void ReloadInteractions() { interactions.Reload(PluginDirectory); nextIdle = DateTimeOffset.UtcNow + interactions.NextDelay(Settings.InteractionFrequency); DataChanged?.Invoke(); }
    public void OpenInteractionFolder() { Directory.CreateDirectory(PluginDirectory); Process.Start(new ProcessStartInfo("explorer.exe", PluginDirectory) { UseShellExecute = true }); }
    public void Say(string text) { SpeechText.Text = text.Replace("{petName}", Settings.PetName); SpeechBubble.Visibility = Visibility.Visible; speechUntil = DateTimeOffset.UtcNow.AddSeconds(7); UpdateLayer(); }
    public void RandomInteraction(bool manual)
    {
        var entry = interactions.Pick(new(DateTime.Now.Hour, Quota?.Windows.Any(w => w.Remaining <= 20) == true), DateTimeOffset.UtcNow);
        nextIdle = DateTimeOffset.UtcNow + interactions.NextDelay(Settings.InteractionFrequency);
        if (entry is null) { if (manual) Say("让我想想下一句，先陪你待一会儿。"); return; }
        Say(entry.Text);
        switch (entry.Action)
        {
            case "wave": Gesture(3); break;
            case "jump": Gesture(4); break;
            case "double-jump": Gesture(4, 2); break;
            case "think": Gesture(7); break;
            case "review": Gesture(8); break;
            case "shy": Gesture(6); break;
            case "look-up": Look(0); break;
            case "look-right": Look(4); break;
            case "look-left": Look(12); break;
        }
    }
    void Look(int direction) { lookDirection = direction; lookUntil = DateTimeOffset.UtcNow.AddSeconds(3); }
    public void ConfigureStartup(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("TaitaiDesktop", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("TaitaiDesktop", false);
            Settings.StartAtLogin = enabled; StartupStatus = ""; Settings.Save();
        }
        catch { StartupStatus = "登录启动设置失败，请检查当前用户权限。"; }
    }
    public Rect CurrentWorkArea() => WorkArea();
    public void ToggleChatStatus() { Settings.ShowChatStatus = !Settings.ShowChatStatus; Settings.Save(); SyncGlassVisibility(); DataChanged?.Invoke(); }
    public void OpenCurrentChat()
    {
        Touch(); var chat = CurrentChat; if (chat is null) { ShowChatSettings(); return; }
        try { Process.Start(new ProcessStartInfo(chat.Url) { UseShellExecute = true }); } catch { Say("没能打开聊天，请检查 ChatGPT 是否安装。"); }
    }
    public void ShowChatSettings() { ShowDetails(); details?.SelectChatTab(); }
    public void BindChat(ChatLink? link, string? title = null)
    {
        Settings.ChatUrl = link?.Url; Settings.ChatTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim(); Settings.Save(); lastActivityPoll = default;
        Activity = new(PetActivity.Idle, 0, default, true, link is null ? "正在关联最近活动" : "正在连接会话状态") { ThreadId = link?.ThreadId, Title = Settings.ChatTitle };
        UpdateCompact(); DataChanged?.Invoke();
    }
    static string ShortToolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "工具";
        var pieces = name.Split(new[] { "__", "." }, StringSplitOptions.RemoveEmptyEntries); var text = pieces.LastOrDefault() ?? name; return text[..Math.Min(32, text.Length)];
    }
}
