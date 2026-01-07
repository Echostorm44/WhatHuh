using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace WhatHuh.Gui;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OpenGitHub_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/Echostorm44");
    }

    private void OpenWhisperNet_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/sandrohanea/whisper.net");
    }

    private void OpenSileroVad_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/snakers4/silero-vad");
    }

    private void OpenFlaticon_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.flaticon.com/free-icons/image");
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
