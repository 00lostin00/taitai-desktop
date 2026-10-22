using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Taitai.Desktop;
public partial class ChatStatusWindow : Window
{
    readonly PetWindow pet;
    public ChatStatusWindow(PetWindow pet)
    {
        this.pet = pet; InitializeComponent(); Owner = pet;
        ShowInTaskbar = Environment.GetCommandLineArgs().Contains("--inspect");
        Loaded += (_, _) => Sync(); MouseEnter += (_, _) => pet.Touch();
    }
    public void Sync()
    {
        var s = pet.Settings.Scale; Root.LayoutTransform = new ScaleTransform(s, s); Width = 308 * s; Height = 84 * s;
        var work = pet.CurrentWorkArea(); Left = Math.Clamp(pet.Left + 6 * s, work.Left, Math.Max(work.Left, work.Right - Width));
        var preferred = pet.Top - 88 * s;
        Top = preferred >= work.Top ? preferred : Math.Clamp(pet.Top + pet.Height + 4 * s, work.Top, Math.Max(work.Top, work.Bottom - Height));
        Topmost = pet.Topmost;
    }
    public void Update()
    {
        Heading.Text = pet.ChatHeading; Status.Text = pet.ChatStatusLine;
        ChatCard.ToolTip = pet.ChatHeading + "\n" + pet.ChatStatusLine + "\n" + (pet.CurrentChat is null ? "点击关联聊天" : "点击返回此聊天");
        Sync();
    }
    void OpenChat(object sender, RoutedEventArgs e) => pet.OpenCurrentChat();
}
