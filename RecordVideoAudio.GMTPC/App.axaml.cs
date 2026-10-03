using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RecordVideoAudio.GMTPC.ViewModels;
using RecordVideoAudio.GMTPC.Views;

namespace RecordVideoAudio.GMTPC;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var vm = new MainViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;

            desktop.Exit += (s, e) =>
            {
                vm.Dispose();
                System.Environment.Exit(0);
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () =>
            {
                try
                {
                    return new MainView { DataContext = new MainViewModel() };
                }
                catch (System.Exception ex)
                {
                    return CreateCrashView(ex);
                }
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            try
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = new MainViewModel()
                };
            }
            catch (System.Exception ex)
            {
                singleViewPlatform.MainView = CreateCrashView(ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Avalonia.Controls.Control CreateCrashView(System.Exception ex)
    {
        var grid = new Avalonia.Controls.Grid
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(15, 15, 22)),
            RowDefinitions = new Avalonia.Controls.RowDefinitions("Auto,Auto,*,Auto")
        };

        var title = new Avalonia.Controls.TextBlock
        {
            Text = "🚨 GMTPC - LỖI KHỞI ĐỘNG GIAO DIỆN",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 60, 80)),
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(16, 24, 16, 8)
        };
        Avalonia.Controls.Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var sub = new Avalonia.Controls.TextBlock
        {
            Text = $"Đã xảy ra lỗi khi khởi tạo ứng dụng: {ex.GetType().Name}\n{ex.Message}",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(180, 180, 200)),
            FontSize = 13,
            Margin = new Avalonia.Thickness(16, 0, 16, 12),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Avalonia.Controls.Grid.SetRow(sub, 1);
        grid.Children.Add(sub);

        var text = new Avalonia.Controls.TextBox
        {
            Text = ex.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(230, 230, 240)),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(25, 25, 35)),
            FontFamily = new Avalonia.Media.FontFamily("Consolas, monospace"),
            FontSize = 11,
            Margin = new Avalonia.Thickness(16, 0, 16, 16)
        };
        Avalonia.Controls.Grid.SetRow(text, 2);
        grid.Children.Add(text);

        var btn = new Avalonia.Controls.Button
        {
            Content = "📋 Chọn toàn bộ nội dung lỗi để sao chép",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(16, 0, 16, 24),
            Padding = new Avalonia.Thickness(24, 8)
        };
        btn.Click += (s, e) =>
        {
            try
            {
                text.Focus();
                text.SelectAll();
                btn.Content = "✅ Đã bôi đen toàn bộ (Chạm giữ để sao chép)!";
            }
            catch { }
        };
        Avalonia.Controls.Grid.SetRow(btn, 3);
        grid.Children.Add(btn);

        return grid;
    }
}