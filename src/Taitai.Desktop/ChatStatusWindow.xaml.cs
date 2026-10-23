using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Taitai.Desktop;
public partial class ChatStatusWindow : Window
{
    readonly PetWindow pet;
    double appliedScale = double.NaN;
    string? lastHeading, lastStatus, lastTooltip;
    public ChatStatusWindow(PetWindow pet)
    {
        this.pet = pet; InitializeComponent(); Owner = pet; Topmost = pet.Topmost;
        ShowInTaskbar = Environment.GetCommandLineArgs().Contains("--inspect");
        Loaded += (_, _) => Sync(); DpiChanged += (_, _) => Sync(); MouseEnter += (_, _) => pet.Touch();
    }
    public void Sync()
    {
        var s = pet.Settings.Scale;
        if (appliedScale != s)
        {
            appliedScale = s; Root.LayoutTransform = new ScaleTransform(s, s);
            Width = 308 * s; Height = 84 * s;
        }
        var work = pet.CurrentWorkArea(); Left = Math.Clamp(pet.Left + 6 * s, work.Left, Math.Max(work.Left, work.Right - Width));
        var preferred = pet.Top - 88 * s;
        Top = preferred >= work.Top ? preferred : Math.Clamp(pet.Top + pet.Height + 4 * s, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }
    public void Update()
    {
        var heading = pet.ChatHeading; var status = pet.ChatStatusLine;
        var tooltip = heading + "\n" + status + "\n" + (pet.CurrentChat is null ? "点击关联聊天" : "点击返回此聊天");
        if (lastHeading != heading) { lastHeading = heading; Heading.Text = heading; }
        if (lastStatus != status) { lastStatus = status; Status.Text = status; }
        if (lastTooltip != tooltip) { lastTooltip = tooltip; ChatCard.ToolTip = tooltip; }
    }
    void OpenChat(object sender, RoutedEventArgs e) => pet.OpenCurrentChat();
}
