using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taitai.Core;

namespace Taitai.Desktop;
public partial class DetailWindow : Window
{
    readonly PetWindow owner;
    bool ready;
    QuotaSnapshot? rendered;
    string? recentSignature;
    readonly List<(QuotaWindow Window, TextBlock Countdown, TextBlock Percent, ProgressBar Bar)> rows = new();
    public DetailWindow(PetWindow owner)
    {
        this.owner = owner; InitializeComponent();
        PointerCheck.IsChecked = owner.Settings.FollowPointer;
        TuckCheck.IsChecked = owner.Settings.EdgeTuck; NoticeCheck.IsChecked = owner.Settings.LowQuotaNotice;
        MotionCheck.IsChecked = owner.Settings.ReduceMotion; SizeSlider.Value = owner.Settings.Scale;
        IdleCheck.IsChecked = owner.Settings.IdleInteraction; FullscreenCheck.IsChecked = owner.Settings.FullscreenAvoidance; HotkeyCheck.IsChecked = owner.Settings.GlobalHotkey;
        NameCheck.IsChecked = owner.Settings.ShowName; NameInput.Text = owner.Settings.PetName;
        ResetCheck.IsChecked = owner.Settings.ResetNotice; PowerCheck.IsChecked = owner.Settings.BatterySaver; StartupCheck.IsChecked = owner.Settings.StartAtLogin;
        FrequencyCombo.SelectedIndex = owner.Settings.InteractionFrequency switch { "quiet" => 0, "lively" => 2, _ => 1 };
        LayerCombo.SelectedIndex = owner.Settings.LayerMode switch { "always" => 1, "normal" => 2, _ => 0 };
        ChatStatusCheck.IsChecked = owner.Settings.ShowChatStatus; ChatLinkInput.Text = owner.CurrentChat?.Url ?? ""; ChatTitleInput.Text = owner.ChatHeading;
        ready = true; owner.DataChanged += Update; Closed += (_, _) => owner.DataChanged -= Update;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Update();
    }
    void Update()
    {
        ActivityLabel.Text = owner.Activity.Label + (owner.Activity.IsStale ? " · 以本机会话事件为准" : "");
        if (rendered != owner.Quota)
        {
            rendered = owner.Quota; rows.Clear(); QuotaRows.Children.Clear();
            if (rendered is null) QuotaRows.Children.Add(new TextBlock { Text = "尚未取得额度", Margin = new Thickness(0, 8, 0, 18), Foreground = PetWindow.Brush("#677268") });
            else foreach (var window in rendered.Windows)
                {
                    var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
                    var line = new DockPanel();
                    var percent = new TextBlock { Text = $"剩余 {window.Remaining:0}%", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
                    DockPanel.SetDock(percent, Dock.Right); line.Children.Add(percent);
                    line.Children.Add(new TextBlock { Text = $"{window.Bucket} · {window.DurationLabel}" });
                    stack.Children.Add(line);
                    var bar = new ProgressBar { Value = window.Remaining, Margin = new Thickness(0, 8, 0, 7), Foreground = PetWindow.Brush(window.Remaining <= 20 ? "#AF7049" : "#71885B") };
                    stack.Children.Add(bar);
                    var countdown = new TextBlock { FontSize = 11, Foreground = PetWindow.Brush("#677268"), TextWrapping = TextWrapping.Wrap };
                    stack.Children.Add(countdown); QuotaRows.Children.Add(stack); rows.Add((window, countdown, percent, bar));
                }
        }
        foreach (var row in rows)
        {
            row.Countdown.Text = QuotaParser.Countdown(row.Window.ResetAt, DateTimeOffset.UtcNow);
            row.Countdown.ToolTip = row.Window.ResetAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "服务未返回重置时间";
            row.Bar.ToolTip = $"已用 {row.Window.UsedPercent:0.#}%，剩余 {row.Window.Remaining:0.#}%";
        }
        if (owner.Quota is null) FreshnessText.Text = owner.Refreshing ? "正在连接本机 Codex…" : "本机 Codex 尚未提供额度。";
        else
        {
            var age = DateTimeOffset.UtcNow - owner.Quota.FetchedAt;
            FreshnessText.Text = $"更新于 {owner.Quota.FetchedAt.ToLocalTime():HH:mm:ss}" + (age.TotalMinutes > 5 ? " · 数据已过期" : " · 自动同步") + (owner.Quota.Plan is string p ? $" · {p}" : "");
        }
        ErrorText.Text = owner.QuotaError ?? ""; ErrorText.Visibility = owner.QuotaError is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = !owner.Refreshing; RefreshButton.Content = owner.Refreshing ? "正在刷新…" : "刷新额度";
        ScaleText.Text = $"{owner.Settings.Scale * 100:0}%";
        HotkeyText.Text = owner.HotkeyStatus; PluginText.Text = owner.PluginStatus; StartupText.Text = owner.StartupStatus;
        ChatHeadingText.Text = owner.ChatHeading; ChatStatusText.Text = owner.ChatStatusLine; ChatStatusCheck.IsChecked = owner.Settings.ShowChatStatus;
        ChatSourceText.Text = owner.CurrentChat is null ? "尚未关联聊天，等待本机会话事件。" : owner.CurrentChat.IsLocal ? (owner.Settings.ChatUrl is null ? "跟随最近的本机活动，等待回复优先。" : "已固定此本机聊天，其他聊天不会覆盖它。") : "已关联 ChatGPT 网页历史；此聊天的实时状态尚未接入。";
        var signature = string.Join("|", owner.RecentChats.Select(x => x.ThreadId + "/" + x.Title));
        if (signature != recentSignature)
        {
            recentSignature = signature; RecentChatRows.Children.Clear();
            foreach (var chat in owner.RecentChats)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var choose = new Button { Content = "关联", Padding = new Thickness(8, 4, 8, 4), MinHeight = 28, Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(choose, Dock.Right);
                choose.Click += (_, _) => { var target = ChatLink.Local(chat.ThreadId); if (target is null) return; owner.BindChat(target, chat.Title); ChatLinkInput.Text = target.Url; ChatTitleInput.Text = chat.Title ?? "本机聊天"; ChatErrorText.Text = "已关联。"; };
                row.Children.Add(choose); row.Children.Add(new TextBlock { Text = chat.Title ?? "本机聊天", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = chat.Title ?? chat.ThreadId }); RecentChatRows.Children.Add(row);
            }
            if (owner.RecentChats.Count == 0) RecentChatRows.Children.Add(new TextBlock { Text = "暂未读到本机聊天。", FontSize = 11, Foreground = PetWindow.Brush("#677268") });
        }
    }
    void CloseDetails(object sender, RoutedEventArgs e) => Close();
    async void RefreshQuota(object sender, RoutedEventArgs e) => await owner.RefreshQuotaAsync();
    void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        owner.Settings.FollowPointer = PointerCheck.IsChecked == true;
        owner.Settings.EdgeTuck = TuckCheck.IsChecked == true; owner.Settings.LowQuotaNotice = NoticeCheck.IsChecked == true;
        owner.Settings.ReduceMotion = MotionCheck.IsChecked == true;
        owner.Settings.ResetNotice = ResetCheck.IsChecked == true; owner.Settings.BatterySaver = PowerCheck.IsChecked == true;
        owner.Settings.IdleInteraction = IdleCheck.IsChecked == true; owner.Settings.FullscreenAvoidance = FullscreenCheck.IsChecked == true;
        owner.Settings.GlobalHotkey = HotkeyCheck.IsChecked == true; owner.Settings.ShowName = NameCheck.IsChecked == true; owner.ApplySettings(); Update();
    }
    void SizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!ready) return; owner.Settings.Scale = e.NewValue; owner.ApplySettings(); ScaleText.Text = $"{e.NewValue * 100:0}%"; }
    void ChooseBinary(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择 Codex CLI 的 codex.exe", Filter = "Codex 命令行 (codex.exe)|codex.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) { owner.Settings.CodexBinary = dialog.FileName; owner.Settings.Save(); _ = owner.RefreshQuotaAsync(); }
    }
    void ExitPet(object sender, RoutedEventArgs e) => owner.Close();
    void ComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return; owner.Settings.InteractionFrequency = FrequencyCombo.SelectedIndex switch { 0 => "quiet", 2 => "lively", _ => "normal" };
        owner.Settings.LayerMode = LayerCombo.SelectedIndex switch { 1 => "always", 2 => "normal", _ => "follow" }; owner.ApplySettings(); owner.ReloadInteractions();
    }
    void ChatNow(object sender, RoutedEventArgs e) => owner.RandomInteraction(true);
    void EasterEgg(object sender, RoutedEventArgs e) { owner.Say("小小毛线团，也有大弹力！"); owner.Gesture(4, 2); }
    void OpenPlugins(object sender, RoutedEventArgs e) => owner.OpenInteractionFolder();
    void ReloadPlugins(object sender, RoutedEventArgs e) { owner.ReloadInteractions(); Update(); }
    void Rename(object sender, RoutedEventArgs e) { var name = NameInput.Text.Trim(); owner.Settings.PetName = string.IsNullOrWhiteSpace(name) ? "苔苔" : name; NameInput.Text = owner.Settings.PetName; owner.ApplySettings(); }
    void StartupChanged(object sender, RoutedEventArgs e) { if (!ready) return; owner.ConfigureStartup(StartupCheck.IsChecked == true); StartupCheck.IsChecked = owner.Settings.StartAtLogin; Update(); }
    public void SelectChatTab() => ChatTab.IsSelected = true;
    void ReturnChat(object sender, RoutedEventArgs e) => owner.OpenCurrentChat();
    void ChatStatusChanged(object sender, RoutedEventArgs e) { if (ready && owner.Settings.ShowChatStatus != (ChatStatusCheck.IsChecked == true)) owner.ToggleChatStatus(); }
    void BindChat(object sender, RoutedEventArgs e)
    {
        var link = ChatLink.Parse(ChatLinkInput.Text); if (link is null) { ChatErrorText.Text = "请输入 codex://threads/聊天ID 或 https://chatgpt.com/c/聊天ID。"; return; }
        owner.BindChat(link, ChatTitleInput.Text); ChatLinkInput.Text = link.Url; ChatErrorText.Text = "已关联；点击“回到这个 ChatGPT 聊天”继续。";
    }
    void FollowRecent(object sender, RoutedEventArgs e) { owner.BindChat(null); ChatLinkInput.Text = ""; ChatTitleInput.Text = ""; ChatErrorText.Text = "已恢复跟随最近的本机活动。"; }
}
