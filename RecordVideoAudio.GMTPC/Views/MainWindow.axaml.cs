using System;
using Avalonia.Controls;
using RecordVideoAudio.GMTPC.ViewModels;

namespace RecordVideoAudio.GMTPC.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            try
            {
                vm.Dispose();
            }
            catch { }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Đảm bảo không còn bất kỳ thread hay tiến trình ngầm nào chạy sau khi tắt cửa sổ chính
        try
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
            }
        }
        catch { }

        Environment.Exit(0);
    }
}