using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Taitai.Core;

namespace Taitai.Desktop;
public partial class GlassBarWindow : Window
{
    readonly PetWindow pet;
    public GlassBarWindow(PetWindow pet)
    {
        this.pet = pet; InitializeComponent(); Owner = pet;
        ShowInTaskbar = Environment.GetCommandLineArgs().Contains("--inspect");
        Loaded += (_, _) => Sync(); DpiChanged += (_, _) => Sync(); MouseEnter += (_, _) => pet.Touch();
    }
    public void Sync()
    {
        var s = pet.Settings.Scale; Root.LayoutTransform = new ScaleTransform(s, s);
        Width = 246 * s; Height = 60 * s; Left = pet.Left + 37 * s; Top = pet.Top + 254 * s; Topmost = pet.Topmost;
    }
    public void Update()
    {
        var windows = pet.Quota?.Windows.Take(2).ToArray() ?? Array.Empty<QuotaWindow>();
        var stale = pet.Quota is not null && (pet.QuotaError is not null || (DateTimeOffset.UtcNow - pet.Quota.FetchedAt).TotalMinutes > 5);
        var buttons = new[] { ShortButton, WeekButton };
        for (int i = 0; i < 2; i++)
        {
            var w = windows.ElementAtOrDefault(i); var b = buttons[i];
            b.Content = w is null ? (i == 0 && pet.Refreshing ? "查询中" : i == 0 ? "未连接" : "每周 —") : $"{w.DurationLabel} {w.Remaining:0}%{(stale ? "*" : "")}";
            b.Foreground = PetWindow.Brush(w?.Remaining <= 20 ? "#9C482D" : "#26372B");
            b.ToolTip = w is null ? pet.QuotaError ?? "点击查看详情或重试" : $"剩余 {w.Remaining:0.#}% · {QuotaParser.Countdown(w.ResetAt, DateTimeOffset.UtcNow)}\n{(w.ResetAt?.ToLocalTime().ToString("MM-dd HH:mm") ?? "重置时间未知")} · {(stale ? "上次数据" : "点击查看详情")}";
        }
        StatusButton.Content = pet.Activity.State switch { PetActivity.Thinking => "正在思考", PetActivity.Tool => "运行工具", PetActivity.Waiting => "等你回应", PetActivity.Complete => "本轮完成", PetActivity.Failed => "任务中断", _ => pet.Activity.Label switch { "等待 Codex 会话" or "未找到本机会话" => "待开工", "未接入聊天状态" => "未接入", "状态未更新" => "待同步", _ => "陪你待着" } };
        StatusButton.ToolTip = pet.ChatHeading + "\n" + pet.ChatStatusLine + "\n点击展开或收起上方聊天状态";
        ChatButton.ToolTip = pet.CurrentChat is null ? "选择要关联的 ChatGPT 聊天" : "返回聊天：" + pet.ChatHeading;
    }
    void OpenDetails(object sender, RoutedEventArgs e) => pet.ShowDetails();
    void OpenChat(object sender, RoutedEventArgs e) => pet.OpenCurrentChat();
    void ToggleChat(object sender, RoutedEventArgs e) => pet.ToggleChatStatus();
}
